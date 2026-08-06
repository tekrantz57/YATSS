using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace YATSS
{
    public enum SpeechBackendMode
    {
        Automatic,
        WindowsSapi,
        LinuxHelper,
        None
    }

    internal interface ISpeechBackend : IDisposable
    {
        IReadOnlyList<string> GetVoices();
        void Speak(string phrase, string voiceName, int? rate);
    }

    internal static class SpeechBackendFactory
    {
        public static ISpeechBackend? Create(SpeechBackendMode mode)
        {
            return mode switch
            {
                SpeechBackendMode.Automatic => CreateAutomatic(),
                SpeechBackendMode.WindowsSapi => SapiSpeechBackend.TryCreate(),
                SpeechBackendMode.LinuxHelper => LinuxSpeechBackend.TryCreate(),
                _ => null
            };
        }

        private static ISpeechBackend? CreateAutomatic()
        {
            ISpeechBackend? sapi = SapiSpeechBackend.TryCreate();
            if (sapi != null)
            {
                try
                {
                    if (sapi.GetVoices().Count > 0)
                    {
                        return sapi;
                    }
                }
                catch
                {
                }

                sapi.Dispose();
            }

            return LinuxSpeechBackend.TryCreate();
        }
    }

    internal sealed class SapiSpeechBackend : ISpeechBackend
    {
        private object? _voice;
        private string _activeVoiceName = "";

        private SapiSpeechBackend(object voice)
        {
            _voice = voice;
        }

        public static SapiSpeechBackend? TryCreate()
        {
            try
            {
                Type? voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                object? voice = voiceType == null ? null : Activator.CreateInstance(voiceType);
                return voice == null ? null : new SapiSpeechBackend(voice);
            }
            catch
            {
                return null;
            }
        }

        public IReadOnlyList<string> GetVoices()
        {
            List<string> voices = new();
            if (_voice == null)
            {
                return voices;
            }

            dynamic voice = _voice;
            dynamic installedVoices = voice.GetVoices();
            for (int i = 0; i < installedVoices.Count; i++)
            {
                string? description = installedVoices.Item(i).GetDescription();
                if (!string.IsNullOrWhiteSpace(description))
                {
                    voices.Add(description);
                }
            }

            return voices;
        }

        public void Speak(string phrase, string voiceName, int? rate)
        {
            if (_voice == null || string.IsNullOrWhiteSpace(phrase))
            {
                return;
            }

            dynamic voice = _voice;
            ApplyVoice(voice, voiceName);
            int? originalRate = null;
            try
            {
                if (rate.HasValue)
                {
                    originalRate = voice.Rate;
                    voice.Rate = Math.Clamp(rate.Value, -10, 10);
                }

                voice.Speak(phrase);
            }
            finally
            {
                if (originalRate.HasValue)
                {
                    voice.Rate = originalRate.Value;
                }
            }
        }

        private void ApplyVoice(dynamic voice, string voiceName)
        {
            if (string.IsNullOrWhiteSpace(voiceName) ||
                string.Equals(_activeVoiceName, voiceName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            dynamic installedVoices = voice.GetVoices();
            for (int i = 0; i < installedVoices.Count; i++)
            {
                dynamic candidate = installedVoices.Item(i);
                string? description = candidate.GetDescription();
                if (string.Equals(description, voiceName, StringComparison.OrdinalIgnoreCase))
                {
                    voice.Voice = candidate;
                    _activeVoiceName = voiceName;
                    return;
                }
            }
        }

        public void Dispose()
        {
            object? voice = Interlocked.Exchange(ref _voice, null);
            if (voice != null && Marshal.IsComObject(voice))
            {
                try
                {
                    Marshal.FinalReleaseComObject(voice);
                }
                catch
                {
                }
            }
        }
    }

    internal sealed class LinuxSpeechBackend : ISpeechBackend
    {
        private readonly LinuxSpeechHelperClient _client = new();

        private LinuxSpeechBackend()
        {
        }

        public static LinuxSpeechBackend? TryCreate()
        {
            LinuxSpeechBackend backend = new();
            try
            {
                return backend.GetVoices().Count > 0 ? backend : null;
            }
            catch
            {
                backend.Dispose();
                return null;
            }
        }

        public IReadOnlyList<string> GetVoices() => _client.GetVoices();

        public void Speak(string phrase, string voiceName, int? rate) =>
            _client.Speak(phrase, voiceName, rate);

        public void Dispose()
        {
        }
    }

    internal sealed class LinuxSpeechHelperClient
    {
        public const int Port = 38591;
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
        private const int VoiceResponseTimeoutMilliseconds = 1500;
        private const int SpeechResponseTimeoutMilliseconds = 30000;
        private readonly int _port;

        public LinuxSpeechHelperClient(int port = Port)
        {
            _port = port;
        }

        public IReadOnlyList<string> GetVoices()
        {
            using JsonDocument response = Send(
                new { protocol = 1, command = "voices" },
                VoiceResponseTimeoutMilliseconds);
            JsonElement root = response.RootElement;
            EnsureSuccess(root);
            if (!root.TryGetProperty("voices", out JsonElement voicesElement) ||
                voicesElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return voicesElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim() ?? "")
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public void Speak(string phrase, string voiceName, int? rate)
        {
            using JsonDocument response = Send(
                new
                {
                    protocol = 1,
                    command = "speak",
                    text = phrase,
                    voice = voiceName,
                    rate
                },
                SpeechResponseTimeoutMilliseconds);
            EnsureSuccess(response.RootElement);
        }

        private JsonDocument Send(object request, int responseTimeoutMilliseconds)
        {
            using TcpClient client = new();
            client.ConnectAsync(IPAddress.Loopback, _port)
                .WaitAsync(ConnectTimeout)
                .GetAwaiter()
                .GetResult();
            client.ReceiveTimeout = responseTimeoutMilliseconds;
            client.SendTimeout = VoiceResponseTimeoutMilliseconds;

            using NetworkStream stream = client.GetStream();
            using StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(request));
            string response = reader.ReadLine()
                ?? throw new IOException("The Linux speech helper closed the connection without responding.");
            return JsonDocument.Parse(response);
        }

        private static void EnsureSuccess(JsonElement response)
        {
            if (response.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True)
            {
                return;
            }

            string message = response.TryGetProperty("error", out JsonElement error)
                ? error.GetString() ?? "Linux speech helper failed."
                : "Linux speech helper returned an invalid response.";
            throw new IOException(message);
        }
    }
}
