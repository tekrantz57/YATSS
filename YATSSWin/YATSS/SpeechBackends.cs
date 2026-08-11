using System.Diagnostics;
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
        Piper,
        LinuxHelper,
        None
    }

    internal interface ISpeechBackend : IDisposable
    {
        IReadOnlyList<string> GetVoices();
        void WarmUp(string voiceName);
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
                SpeechBackendMode.Piper => PiperSpeechBackend.TryCreate(),
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

            ISpeechBackend? piper = PiperSpeechBackend.TryCreate();
            return piper ?? LinuxSpeechBackend.TryCreate();
        }
    }

    internal sealed class PiperSpeechBackend : ISpeechBackend
    {
        private readonly SpeechHelperClient _client = new(PiperHelperLauncher.Port);

        private PiperSpeechBackend()
        {
        }

        public static PiperSpeechBackend? TryCreate()
        {
            PiperSpeechBackend backend = new();
            try
            {
                PiperHelperLauncher.EnsureAvailable(backend._client);
                return backend.GetVoices().Count > 0 ? backend : null;
            }
            catch
            {
                backend.Dispose();
                return null;
            }
        }

        public IReadOnlyList<string> GetVoices() => _client.GetVoices();

        public void WarmUp(string voiceName) => _client.WarmUp(voiceName);

        public void Speak(string phrase, string voiceName, int? rate) =>
            _client.Speak(phrase, voiceName, rate);

        public void Dispose()
        {
        }
    }

    internal static class PiperHelperLauncher
    {
        public const int Port = 38592;
        private static readonly object SyncRoot = new();
        private static Process? _process;

        static PiperHelperLauncher()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
        }

        public static void EnsureAvailable(SpeechHelperClient client)
        {
            if (client.Ping())
            {
                return;
            }

            if (PlatformEnvironment.IsWine)
            {
                throw new IOException("The native Linux Piper helper is not running.");
            }

            lock (SyncRoot)
            {
                if (client.Ping())
                {
                    return;
                }

                if (_process is not { HasExited: false })
                {
                    _process?.Dispose();
                    _process = Start();
                }
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (client.Ping())
                {
                    return;
                }

                Thread.Sleep(100);
            }

            throw new IOException("The Piper speech helper did not start.");
        }

        private static Process Start()
        {
            string helperPath = Path.Combine(
                AppContext.BaseDirectory,
                "Linux",
                "yatss-speech-helper.py");
            if (!File.Exists(helperPath))
            {
                throw new FileNotFoundException("The packaged Piper speech helper was not found.", helperPath);
            }

            string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string voiceDirectory = Environment.GetEnvironmentVariable("YATSS_PIPER_VOICE_DIR")
                ?? Path.Combine(localApplicationData, "YATSS", "PiperVoices");
            Directory.CreateDirectory(voiceDirectory);

            ProcessStartInfo startInfo = new()
            {
                FileName = Environment.GetEnvironmentVariable("YATSS_PYTHON") ?? "python",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = voiceDirectory
            };
            startInfo.ArgumentList.Add(helperPath);
            startInfo.ArgumentList.Add("--engine");
            startInfo.ArgumentList.Add("piper");
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(Port.ToString());
            startInfo.ArgumentList.Add("--data-dir");
            startInfo.ArgumentList.Add(voiceDirectory);

            return Process.Start(startInfo)
                ?? throw new IOException("Python did not start the Piper speech helper.");
        }

        private static void Stop()
        {
            lock (SyncRoot)
            {
                Process? process = Interlocked.Exchange(ref _process, null);
                if (process == null)
                {
                    return;
                }

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
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

        public void WarmUp(string voiceName)
        {
            if (_voice != null)
            {
                ApplyVoice((dynamic)_voice, voiceName);
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
        private readonly SpeechHelperClient _client = new();

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

        public void WarmUp(string voiceName)
        {
        }

        public void Speak(string phrase, string voiceName, int? rate) =>
            _client.Speak(phrase, voiceName, rate);

        public void Dispose()
        {
        }
    }

    internal sealed class SpeechHelperClient
    {
        public const int Port = 38591;
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
        private const int VoiceResponseTimeoutMilliseconds = 1500;
        private const int SpeechResponseTimeoutMilliseconds = 30000;
        private readonly int _port;

        public SpeechHelperClient(int port = Port)
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

        public bool Ping()
        {
            try
            {
                using JsonDocument response = Send(
                    new { protocol = 1, command = "ping" },
                    VoiceResponseTimeoutMilliseconds);
                JsonElement root = response.RootElement;
                EnsureSuccess(root);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void WarmUp(string voiceName)
        {
            using JsonDocument response = Send(
                new { protocol = 1, command = "warmup", voice = voiceName },
                SpeechResponseTimeoutMilliseconds);
            EnsureSuccess(response.RootElement);
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
                ?? throw new IOException("The speech helper closed the connection without responding.");
            return JsonDocument.Parse(response);
        }

        private static void EnsureSuccess(JsonElement response)
        {
            if (response.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True)
            {
                return;
            }

            string message = response.TryGetProperty("error", out JsonElement error)
                ? error.GetString() ?? "The speech helper failed."
                : "The speech helper returned an invalid response.";
            throw new IOException(message);
        }
    }
}
