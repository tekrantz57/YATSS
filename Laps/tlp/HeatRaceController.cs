namespace tlp
{
    public enum HeatRaceState
    {
        Practice,
        Ready,
        Running,
        Paused,
        Complete
    }

    public sealed record HeatRaceEdgeDecision(
        bool ShouldProcess,
        LapEdge Edge,
        bool FastestLapEligible,
        string Detail);

    public sealed class HeatRaceController
    {
        public const int TotalHeats = 8;

        private readonly bool[] _laneSeenThisHeat = new bool[LapProtocolParser.LaneCount];
        private readonly object _gate = new();
        private long _heatLengthMilliseconds;
        private int _betweenHeatsSeconds;
        private long _activeMillisecondsBeforeRun;
        private long _raceTimestampBase;
        private uint _runStartedAt;
        private bool _hasRunStartedAt;
        private bool _isFirstHeat = true;

        public HeatRaceState State { get; private set; } = HeatRaceState.Practice;
        public int HeatNumber { get; private set; }
        public bool HasMoreHeats => HeatNumber > 0 && HeatNumber < TotalHeats;
        public int BetweenHeatsSeconds
        {
            get
            {
                lock (_gate)
                {
                    return _betweenHeatsSeconds;
                }
            }
        }

        public int HeatLengthMinutes
        {
            get
            {
                lock (_gate)
                {
                    return (int)Math.Max(1, _heatLengthMilliseconds / 60000);
                }
            }
        }

        public void Configure(int heatLengthMinutes, int betweenHeatsSeconds)
        {
            lock (_gate)
            {
                _heatLengthMilliseconds = Math.Max(1, heatLengthMinutes) * 60000L;
                _betweenHeatsSeconds = Math.Clamp(betweenHeatsSeconds, 0, 300);
                _activeMillisecondsBeforeRun = 0;
                _raceTimestampBase = 0;
                _hasRunStartedAt = false;
                HeatNumber = 1;
                _isFirstHeat = true;
                Array.Fill(_laneSeenThisHeat, false);
                State = HeatRaceState.Ready;
            }
        }

        public void SetPracticeMode()
        {
            lock (_gate)
            {
                State = HeatRaceState.Practice;
                _activeMillisecondsBeforeRun = 0;
                _raceTimestampBase = 0;
                _hasRunStartedAt = false;
                HeatNumber = 0;
                Array.Fill(_laneSeenThisHeat, false);
            }
        }

        public bool Start(uint controllerTimestamp)
        {
            lock (_gate)
            {
                if (State != HeatRaceState.Ready)
                {
                    return false;
                }

                _runStartedAt = controllerTimestamp;
                _hasRunStartedAt = true;
                Array.Fill(_laneSeenThisHeat, false);
                State = HeatRaceState.Running;
                return true;
            }
        }

        public bool Pause(uint controllerTimestamp)
        {
            lock (_gate)
            {
                if (State != HeatRaceState.Running)
                {
                    return false;
                }

                _activeMillisecondsBeforeRun = GetElapsedMillisecondsCore(controllerTimestamp);
                _hasRunStartedAt = false;
                State = HeatRaceState.Paused;
                return true;
            }
        }

        public bool Resume(uint controllerTimestamp)
        {
            lock (_gate)
            {
                if (State != HeatRaceState.Paused)
                {
                    return false;
                }

                _runStartedAt = controllerTimestamp;
                _hasRunStartedAt = true;
                State = HeatRaceState.Running;
                return true;
            }
        }

        public bool Complete()
        {
            lock (_gate)
            {
                if (State != HeatRaceState.Running && State != HeatRaceState.Paused)
                {
                    return false;
                }

                _raceTimestampBase += _heatLengthMilliseconds;
                _activeMillisecondsBeforeRun = _heatLengthMilliseconds;
                _hasRunStartedAt = false;
                State = HeatRaceState.Complete;
                return true;
            }
        }

        public bool PrepareNextHeat()
        {
            lock (_gate)
            {
                if (State != HeatRaceState.Complete || !HasMoreHeats)
                {
                    return false;
                }

                HeatNumber++;
                _activeMillisecondsBeforeRun = 0;
                _hasRunStartedAt = false;
                _isFirstHeat = false;
                Array.Fill(_laneSeenThisHeat, false);
                State = HeatRaceState.Ready;
                return true;
            }
        }

        public bool IsExpired(uint controllerTimestamp)
        {
            lock (_gate)
            {
                return State == HeatRaceState.Running &&
                    GetElapsedMillisecondsCore(controllerTimestamp) >= _heatLengthMilliseconds;
            }
        }

        public TimeSpan GetRemaining(uint controllerTimestamp)
        {
            lock (_gate)
            {
                long elapsed = State == HeatRaceState.Running
                    ? GetElapsedMillisecondsCore(controllerTimestamp)
                    : _activeMillisecondsBeforeRun;
                return TimeSpan.FromMilliseconds(Math.Max(0, _heatLengthMilliseconds - elapsed));
            }
        }

        public HeatRaceEdgeDecision PrepareEdge(LapEdge edge)
        {
            lock (_gate)
            {
                if (State != HeatRaceState.Running)
                {
                    return new HeatRaceEdgeDecision(false, edge, false, "heat is not running");
                }

                bool isFirstLaneEdge = !_laneSeenThisHeat[edge.LaneIndex];
                _laneSeenThisHeat[edge.LaneIndex] = true;

                bool fastestLapEligible = !isFirstLaneEdge || _isFirstHeat;
                uint adjustedTimestamp = (uint)(_raceTimestampBase + GetElapsedMillisecondsCore(edge.TimestampMillis));
                LapEdge adjustedEdge = edge with { TimestampMillis = adjustedTimestamp };

                return new HeatRaceEdgeDecision(
                    true,
                    adjustedEdge,
                    fastestLapEligible,
                    isFirstLaneEdge ? "first lane edge in heat" : "heat edge");
            }
        }

        private long GetElapsedMillisecondsCore(uint controllerTimestamp)
        {
            if (!_hasRunStartedAt)
            {
                return _activeMillisecondsBeforeRun;
            }

            uint runElapsed = unchecked(controllerTimestamp - _runStartedAt);
            return _activeMillisecondsBeforeRun + runElapsed;
        }
    }
}
