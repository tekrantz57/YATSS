using System.Collections.Concurrent;

namespace YATSS
{
    internal static class SpeechAnnouncer
    {
        private static readonly TimeSpan SilentCountdownDuration = TimeSpan.FromSeconds(3);
        private static readonly object SyncRoot = new();
        private static BlockingCollection<SpeechRequest>? _requests;
        private static Thread? _worker;
        private static bool _enabled = true;

        public static bool Enabled
        {
            get => Volatile.Read(ref _enabled);
            set => Volatile.Write(ref _enabled, value);
        }

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
            if (!Enabled)
            {
                return;
            }

            EnsureStarted();
            _requests?.Add(SpeechRequest.Single("", voiceName));
        }

        public static void SpeakAsync(string phrase, string voiceName)
        {
            if (!Enabled)
            {
                return;
            }

            EnsureStarted();
            _requests?.Add(SpeechRequest.Single(phrase, voiceName));
        }

        public static void SpeakCountdownAsync(string voiceName, Action? afterSpeech = null)
        {
            if (!Enabled)
            {
                _ = CompleteSilentCountdownAsync(afterSpeech);
                return;
            }

            EnsureStarted();
            _requests?.Add(new SpeechRequest(
                new[] { "3 2 1 Let's go" },
                voiceName,
                TimeSpan.Zero,
                Rate: -2,
                SilentCountdownDuration,
                afterSpeech));
        }

        private static async Task CompleteSilentCountdownAsync(Action? afterSpeech)
        {
            await Task.Delay(SilentCountdownDuration).ConfigureAwait(false);
            try
            {
                afterSpeech?.Invoke();
            }
            catch
            {
            }
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
            int? originalRate = null;

            foreach (SpeechRequest request in requests.GetConsumingEnumerable())
            {
                try
                {
                    if (!Enabled)
                    {
                        Thread.Sleep(request.FallbackDelay);
                        continue;
                    }

                    voice ??= CreateVoice();
                    if (voice == null)
                    {
                        Thread.Sleep(request.FallbackDelay);
                        continue;
                    }

                    if (!string.Equals(activeVoiceName, request.VoiceName, StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyVoice(voice, request.VoiceName);
                        activeVoiceName = request.VoiceName;
                    }

                    if (request.Rate.HasValue)
                    {
                        originalRate = voice.Rate;
                        voice.Rate = Math.Clamp(request.Rate.Value, -10, 10);
                    }

                    for (int i = 0; i < request.Phrases.Count; i++)
                    {
                        string phrase = request.Phrases[i];
                        if (!string.IsNullOrWhiteSpace(phrase))
                        {
                            voice.Speak(phrase);
                        }

                        if (i < request.Phrases.Count - 1 && request.DelayBetweenPhrases > TimeSpan.Zero)
                        {
                            Thread.Sleep(request.DelayBetweenPhrases);
                        }
                    }

                    RestoreRate(voice, ref originalRate);
                }
                catch
                {
                    // Voice announcements are helpful, but they should never affect race control.
                }
                finally
                {
                    try
                    {
                        RestoreRate(voice, ref originalRate);

                        request.AfterSpeech?.Invoke();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static dynamic? CreateVoice()
        {
            Type? voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            return voiceType == null ? null : Activator.CreateInstance(voiceType);
        }

        private static void RestoreRate(object? voice, ref int? originalRate)
        {
            if (voice == null || !originalRate.HasValue)
            {
                return;
            }

            int rate = originalRate.Value;
            object nonNullVoiceObject = voice;
            dynamic nonNullVoice = nonNullVoiceObject;
            nonNullVoice.Rate = rate;
            originalRate = null;
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

        private sealed record SpeechRequest(
            IReadOnlyList<string> Phrases,
            string VoiceName,
            TimeSpan DelayBetweenPhrases,
            int? Rate,
            TimeSpan FallbackDelay,
            Action? AfterSpeech)
        {
            public static SpeechRequest Single(string phrase, string voiceName) =>
                new(new[] { phrase }, voiceName, TimeSpan.Zero, null, TimeSpan.Zero, null);
        }
    }
}
