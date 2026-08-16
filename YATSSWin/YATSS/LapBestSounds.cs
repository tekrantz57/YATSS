using System.Diagnostics;
using System.Media;
using System.Reflection;

namespace YATSS
{
    internal enum LapBestSoundKind
    {
        None,
        Lane,
        Heat
    }

    internal static class LapBestSoundDecision
    {
        public static LapBestSoundKind Select(
            bool enabled,
            LapUpdate update,
            bool heatRunning,
            int? previousHeatBestMilliseconds)
        {
            if (!enabled ||
                !update.FastestLapEligible ||
                !update.LapMilliseconds.HasValue)
            {
                return LapBestSoundKind.None;
            }

            if (heatRunning &&
                (!previousHeatBestMilliseconds.HasValue ||
                 update.LapMilliseconds.Value < previousHeatBestMilliseconds.Value))
            {
                return LapBestSoundKind.Heat;
            }

            return update.ImprovedLaneBest
                ? LapBestSoundKind.Lane
                : LapBestSoundKind.None;
        }
    }

    internal sealed class LapBestSoundPlayer : IDisposable
    {
        private const string LaneResourceName = "YATSS.Assets.Sounds.lane-best.wav";
        private const string HeatResourceName = "YATSS.Assets.Sounds.heat-best.wav";
        // SoundPlayer starts asynchronously. Include enough margin that a new
        // lane event cannot stop a sound before its final pip reaches the device.
        private static readonly long LanePlaybackGuardTicks =
            (long)(Stopwatch.Frequency * 0.300);
        private static readonly long HeatPlaybackGuardTicks =
            (long)(Stopwatch.Frequency * 0.600);

        private readonly object _gate = new();
        private readonly Stream _laneStream;
        private readonly Stream _heatStream;
        private readonly SoundPlayer _lanePlayer;
        private readonly SoundPlayer _heatPlayer;
        private long _busyUntil;
        private LapBestSoundKind _playingKind;
        private bool _disposed;

        private LapBestSoundPlayer()
        {
            Assembly assembly = typeof(LapBestSoundPlayer).Assembly;
            _laneStream = assembly.GetManifestResourceStream(LaneResourceName)
                ?? throw new InvalidOperationException($"Missing embedded sound {LaneResourceName}.");
            _heatStream = assembly.GetManifestResourceStream(HeatResourceName)
                ?? throw new InvalidOperationException($"Missing embedded sound {HeatResourceName}.");
            _lanePlayer = new SoundPlayer(_laneStream);
            _heatPlayer = new SoundPlayer(_heatStream);
            _lanePlayer.Load();
            _heatPlayer.Load();
        }

        public static LapBestSoundPlayer? TryCreate()
        {
            try
            {
                return new LapBestSoundPlayer();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Lap-best sounds are unavailable: {ex.Message}");
                return null;
            }
        }

        public void Play(LapBestSoundKind kind)
        {
            if (kind == LapBestSoundKind.None)
            {
                return;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                long now = Stopwatch.GetTimestamp();
                if (now < _busyUntil &&
                    (kind != LapBestSoundKind.Heat || _playingKind == LapBestSoundKind.Heat))
                {
                    return;
                }

                try
                {
                    _lanePlayer.Stop();
                    _heatPlayer.Stop();
                    SoundPlayer player = kind == LapBestSoundKind.Heat
                        ? _heatPlayer
                        : _lanePlayer;
                    player.Play();
                    _playingKind = kind;
                    _busyUntil = now + (kind == LapBestSoundKind.Heat
                        ? HeatPlaybackGuardTicks
                        : LanePlaybackGuardTicks);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Lap-best sound playback failed: {ex.Message}");
                    _busyUntil = 0;
                    _playingKind = LapBestSoundKind.None;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _lanePlayer.Dispose();
                _heatPlayer.Dispose();
                _laneStream.Dispose();
                _heatStream.Dispose();
            }
        }
    }
}
