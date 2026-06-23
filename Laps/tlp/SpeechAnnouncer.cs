namespace tlp
{
    internal static class SpeechAnnouncer
    {
        public static void SpeakAsync(string phrase)
        {
            _ = Task.Run(() => Speak(phrase));
        }

        private static void Speak(string phrase)
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

                voice.Speak(phrase);
            }
            catch
            {
                // Voice announcements are helpful, but they should never affect race control.
            }
        }
    }
}
