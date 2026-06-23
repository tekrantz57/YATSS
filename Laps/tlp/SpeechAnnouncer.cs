namespace tlp
{
    internal static class SpeechAnnouncer
    {
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

        public static void SpeakAsync(string phrase, string voiceName)
        {
            _ = Task.Run(() => Speak(phrase, voiceName));
        }

        private static void Speak(string phrase, string voiceName)
        {
            try
            {
                Type? voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (voiceType == null)
                {
                    return;
                }

                dynamic? voice = Activator.CreateInstance(voiceType);
                if (voice == null)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(voiceName))
                {
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

                voice.Speak(phrase);
            }
            catch
            {
                // Voice announcements are helpful, but they should never affect race control.
            }
        }
    }
}
