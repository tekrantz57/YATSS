using System.Collections.Concurrent;

namespace YATSS
{
    internal static class SpeechAnnouncer
    {
        private static readonly TimeSpan SilentCountdownDuration = TimeSpan.FromSeconds(1.5);
        private static readonly object SyncRoot = new();
        private static BlockingCollection<SpeechRequest>? _requests;
        private static Thread? _worker;
        private static bool _enabled = true;
        private static SpeechBackendMode _backendMode = SpeechBackendMode.Automatic;

        public static bool Enabled
        {
            get => Volatile.Read(ref _enabled);
            set => Volatile.Write(ref _enabled, value);
        }

        public static SpeechBackendMode BackendMode
        {
            get => _backendMode;
            set => _backendMode = value;
        }

        public static List<string> GetInstalledVoices(SpeechBackendMode mode)
        {
            try
            {
                using ISpeechBackend? backend = SpeechBackendFactory.Create(mode);
                return backend?.GetVoices().ToList() ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public static void WarmUpAsync(string voiceName)
        {
            if (!Enabled || BackendMode == SpeechBackendMode.None)
            {
                return;
            }

            EnsureStarted();
            _requests?.Add(SpeechRequest.Single("", voiceName, BackendMode));
        }

        public static void SpeakAsync(string phrase, string voiceName)
        {
            if (!Enabled || BackendMode == SpeechBackendMode.None)
            {
                return;
            }

            EnsureStarted();
            _requests?.Add(SpeechRequest.Single(phrase, voiceName, BackendMode));
        }

        public static void SpeakCountdownAsync(
            string voiceName,
            Action<int> countdownStep,
            Action? afterSpeech = null)
        {
            EnsureStarted();
            SpeechBackendMode mode = Enabled ? BackendMode : SpeechBackendMode.None;
            _requests?.Add(new SpeechRequest(
                new[] { "3", "2", "1 Let's go" },
                voiceName,
                mode,
                TimeSpan.FromMilliseconds(500),
                Rate: 3,
                SilentCountdownDuration,
                countdownStep,
                afterSpeech));
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
            ISpeechBackend? backend = null;
            SpeechBackendMode? activeMode = null;

            foreach (SpeechRequest request in requests.GetConsumingEnumerable())
            {
                long requestStarted = Environment.TickCount64;
                try
                {
                    if (activeMode != request.BackendMode || backend == null)
                    {
                        backend?.Dispose();
                        backend = SpeechBackendFactory.Create(request.BackendMode);
                        activeMode = request.BackendMode;
                    }

                    for (int i = 0; i < request.Phrases.Count; i++)
                    {
                        long phraseStarted = Environment.TickCount64;
                        try
                        {
                            request.PhraseStarted?.Invoke(i + 1);
                        }
                        catch
                        {
                        }

                        string phrase = request.Phrases[i];
                        if (backend != null && !string.IsNullOrWhiteSpace(phrase))
                        {
                            try
                            {
                                backend.Speak(phrase, request.VoiceName, request.Rate);
                            }
                            catch
                            {
                                backend.Dispose();
                                backend = null;
                            }
                        }

                        if (i < request.Phrases.Count - 1 && request.DelayBetweenPhrases > TimeSpan.Zero)
                        {
                            long phraseElapsed = Environment.TickCount64 - phraseStarted;
                            TimeSpan remainingInterval = request.DelayBetweenPhrases - TimeSpan.FromMilliseconds(phraseElapsed);
                            if (remainingInterval > TimeSpan.Zero)
                            {
                                Thread.Sleep(remainingInterval);
                            }
                        }
                    }
                }
                catch
                {
                    backend?.Dispose();
                    backend = null;
                }
                finally
                {
                    long elapsedMilliseconds = Environment.TickCount64 - requestStarted;
                    TimeSpan remainingDelay = request.FallbackDelay - TimeSpan.FromMilliseconds(elapsedMilliseconds);
                    if (remainingDelay > TimeSpan.Zero)
                    {
                        Thread.Sleep(remainingDelay);
                    }

                    try
                    {
                        request.AfterSpeech?.Invoke();
                    }
                    catch
                    {
                    }
                }
            }

            backend?.Dispose();
        }

        private sealed record SpeechRequest(
            IReadOnlyList<string> Phrases,
            string VoiceName,
            SpeechBackendMode BackendMode,
            TimeSpan DelayBetweenPhrases,
            int? Rate,
            TimeSpan FallbackDelay,
            Action<int>? PhraseStarted,
            Action? AfterSpeech)
        {
            public static SpeechRequest Single(
                string phrase,
                string voiceName,
                SpeechBackendMode backendMode) =>
                new(
                    new[] { phrase },
                    voiceName,
                    backendMode,
                    TimeSpan.Zero,
                    null,
                    TimeSpan.Zero,
                    null,
                    null);
        }
    }
}
