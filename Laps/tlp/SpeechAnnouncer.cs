using System.Collections.Concurrent;

namespace tlp
{
    internal static class SpeechAnnouncer
    {
        private static readonly object SyncRoot = new();
        private static BlockingCollection<SpeechRequest>? _requests;
        private static Thread? _worker;

        public static List<string> GetInstalledVoices()
        {
            List<string> voices = new();
            try
            {
                Type? voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (voiceType == null)
                {
                    return voices;
                }

                dynamic? voice = Activator.CreateInstance(voiceType);
                if (voice == null)
                {
                    return voices;
                }

                dynamic installedVoices = voice.GetVoices();
                for (int i = 0; i < installedVoices.Count; i++)
                {
                    string? description = installedVoices.Item(i).GetDescription();
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        voices.Add(description);
                    }
                }
            }
            catch
            {
            }

            return voices;
        }

        public static void WarmUpAsync(string voiceName)
        {
            EnsureStarted();
            _requests?.Add(new SpeechRequest("", voiceName));
        }

        public static void SpeakAsync(string phrase, string voiceName)
        {
            EnsureStarted();
            _requests?.Add(new SpeechRequest(phrase, voiceName));
        }

        private static void EnsureStarted()
        {
            lock (SyncRoot)
            {
                if (_requests != null)
                {
                    return;
                }

                _requests = new BlockingCollection<SpeechRequest>();
                _worker = new Thread(() => RunWorker(_requests))
                {
                    IsBackground = true,
                    Name = "Speech announcer"
                };
                _worker.SetApartmentState(ApartmentState.STA);
                _worker.Start();
            }
        }

        private static void RunWorker(BlockingCollection<SpeechRequest> requests)
        {
            dynamic? voice = null;
            string activeVoiceName = "";

            foreach (SpeechRequest request in requests.GetConsumingEnumerable())
            {
                try
                {
                    voice ??= CreateVoice();
                    if (voice == null)
                    {
                        continue;
                    }

                    if (!string.Equals(activeVoiceName, request.VoiceName, StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyVoice(voice, request.VoiceName);
                        activeVoiceName = request.VoiceName;
                    }

                    if (!string.IsNullOrWhiteSpace(request.Phrase))
                    {
                        voice.Speak(request.Phrase);
                    }
                }
                catch
                {
                    // Voice announcements are helpful, but they should never affect race control.
                }
            }
        }

        private static dynamic? CreateVoice()
        {
            Type? voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            return voiceType == null ? null : Activator.CreateInstance(voiceType);
        }

        private static void ApplyVoice(dynamic voice, string voiceName)
        {
            if (string.IsNullOrWhiteSpace(voiceName))
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
                    break;
                }
            }
        }

        private sealed record SpeechRequest(string Phrase, string VoiceName);
    }
}
