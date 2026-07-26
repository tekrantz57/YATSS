namespace YATSS
{
    internal sealed class DemoLapTiming
    {
        private const double ReferenceTrackLengthFeet = 155.0;
        private const double FirstBaselineLapFraction = 1.0 / 3.0;
        private const int ReferenceMinimumLapMilliseconds = 4200;
        private const int ReferenceMaximumLapMilliseconds = 6500;
        private static readonly int[] ReferenceLanePaceMilliseconds =
        {
            4300,
            4550,
            4800,
            5050,
            5300,
            5550,
            5800,
            6050
        };

        private readonly object _syncRoot = new();
        private readonly Dictionary<string, int> _racerPaceMilliseconds =
            new(StringComparer.OrdinalIgnoreCase);

        public void ConfigureRacers(IReadOnlyList<string> racers)
        {
            Random random = new(Random.Shared.Next());
            int[] shuffledPaces = CreateLanePaces(random);
            lock (_syncRoot)
            {
                _racerPaceMilliseconds.Clear();
                for (int i = 0; i < racers.Count; i++)
                {
                    string racer = racers[i].Trim();
                    if (racer.Length > 0)
                    {
                        _racerPaceMilliseconds[racer] = shuffledPaces[i % shuffledPaces.Length];
                    }
                }
            }
        }

        public int GetReferencePaceMilliseconds(
            int lane,
            IReadOnlyList<int> lanePaceMilliseconds,
            string racerName)
        {
            if (!string.IsNullOrWhiteSpace(racerName))
            {
                lock (_syncRoot)
                {
                    if (_racerPaceMilliseconds.TryGetValue(racerName, out int racerPace))
                    {
                        return racerPace;
                    }
                }
            }

            return lane >= 0 && lane < lanePaceMilliseconds.Count
                ? lanePaceMilliseconds[lane]
                : ReferenceLanePaceMilliseconds[0];
        }

        public static int GetFirstBaselineMilliseconds(
            Random random,
            int referenceBaseLapMilliseconds,
            double trackLengthFeet,
            int configuredMinimumLapMilliseconds)
        {
            int fullLapMilliseconds = GetLapIntervalMilliseconds(
                random,
                referenceBaseLapMilliseconds,
                trackLengthFeet,
                configuredMinimumLapMilliseconds);
            return Math.Max(1, (int)Math.Round(fullLapMilliseconds * FirstBaselineLapFraction));
        }

        public static int GetLapIntervalMilliseconds(
            Random random,
            int referenceBaseLapMilliseconds,
            double trackLengthFeet,
            int configuredMinimumLapMilliseconds)
        {
            double trackScale = Math.Clamp(trackLengthFeet, 1.0, 10000.0) / ReferenceTrackLengthFeet;
            int minimumLap = Math.Max(
                Math.Max(0, configuredMinimumLapMilliseconds),
                ScaleMilliseconds(ReferenceMinimumLapMilliseconds, trackScale));
            int maximumLap = ScaleMilliseconds(ReferenceMaximumLapMilliseconds, trackScale);
            int baseLap = ScaleMilliseconds(referenceBaseLapMilliseconds, trackScale);
            int interval = baseLap + ScaleMilliseconds(random.Next(-280, 341), trackScale);

            if (random.NextDouble() < 0.14)
            {
                interval -= ScaleMilliseconds(random.Next(120, 281), trackScale);
            }

            if (random.NextDouble() < 0.18)
            {
                interval += ScaleMilliseconds(random.Next(250, 651), trackScale);
            }

            return Math.Clamp(interval, minimumLap, Math.Max(minimumLap, maximumLap));
        }

        public static int[] CreateLanePaces(Random random)
        {
            int[] paces = new int[LapProtocolParser.LaneCount];
            for (int lane = 0; lane < paces.Length; lane++)
            {
                paces[lane] = ReferenceLanePaceMilliseconds[lane % ReferenceLanePaceMilliseconds.Length];
            }

            for (int i = paces.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (paces[i], paces[j]) = (paces[j], paces[i]);
            }

            return paces;
        }

        private static int ScaleMilliseconds(int referenceMilliseconds, double trackScale)
            => Math.Max(1, (int)Math.Round(referenceMilliseconds * trackScale));
    }
}
