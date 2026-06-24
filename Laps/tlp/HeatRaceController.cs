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
        bool CountFirstEdgeAsLap,
        bool FastestLapEligible,
        string Detail);

    public sealed record HeatRaceSnapshot(
        HeatRaceState State,
        int HeatNumber,
        TimeSpan Remaining,
        string OnDeckRacer,
        IReadOnlyList<string> LaneRacers,
        IReadOnlyList<int> LaneLapCounts);

    public sealed class HeatRaceController
    {
        public const int TotalHeats = 8;

        private readonly bool[] _laneSeenThisHeat = new bool[LapProtocolParser.LaneCount];
        private readonly RacerEntry[] _laneRacers = Enumerable.Range(0, LapProtocolParser.LaneCount)
            .Select(_ => new RacerEntry(string.Empty))
            .ToArray();
        private readonly Queue<RacerEntry> _waitingRacers = new();
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

        public void Configure(int heatLengthMinutes, int betweenHeatsSeconds, IReadOnlyList<string> racers)
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
                SetInitialRacers(racers);
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
                ClearRacers();
                _waitingRacers.Clear();
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

        public bool PrepareNextHeat(IReadOnlyList<int> laneLapCounts)
        {
            lock (_gate)
            {
                if (State != HeatRaceState.Complete || !HasMoreHeats)
                {
                    return false;
                }

                UpdateLaneLapCounts(laneLapCounts);
                RotateRacers();
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
                return GetRemainingCore(controllerTimestamp);
            }
        }

        public HeatRaceSnapshot GetSnapshot(uint controllerTimestamp)
        {
            lock (_gate)
            {
                TimeSpan remaining = State == HeatRaceState.Practice
                    ? TimeSpan.Zero
                    : GetRemainingCore(controllerTimestamp);
                return new HeatRaceSnapshot(
                    State,
                    HeatNumber,
                    remaining,
                    _waitingRacers.Count > 0 ? _waitingRacers.Peek().Name : string.Empty,
                    _laneRacers.Select(racer => racer.Name).ToArray(),
                    _laneRacers.Select(racer => racer.LapCount).ToArray());
            }
        }

        public HeatRaceEdgeDecision PrepareEdge(LapEdge edge)
        {
            lock (_gate)
            {
                if (State != HeatRaceState.Running)
                {
                    return new HeatRaceEdgeDecision(false, edge, false, false, "heat is not running");
                }

                bool isFirstLaneEdge = !_laneSeenThisHeat[edge.LaneIndex];
                _laneSeenThisHeat[edge.LaneIndex] = true;

                bool countFirstEdgeAsLap = isFirstLaneEdge && !_isFirstHeat;
                bool fastestLapEligible = !isFirstLaneEdge || _isFirstHeat;
                uint adjustedTimestamp = (uint)(_raceTimestampBase + GetElapsedMillisecondsCore(edge.TimestampMillis));
                LapEdge adjustedEdge = edge with { TimestampMillis = adjustedTimestamp };

                return new HeatRaceEdgeDecision(
                    true,
                    adjustedEdge,
                    countFirstEdgeAsLap,
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

        private TimeSpan GetRemainingCore(uint controllerTimestamp)
        {
            long elapsed = State == HeatRaceState.Running
                ? GetElapsedMillisecondsCore(controllerTimestamp)
                : _activeMillisecondsBeforeRun;
            return TimeSpan.FromMilliseconds(Math.Max(0, _heatLengthMilliseconds - elapsed));
        }

        private void SetInitialRacers(IReadOnlyList<string> racers)
        {
            ClearRacers();
            _waitingRacers.Clear();

            for (int i = 0; i < racers.Count; i++)
            {
                string racer = racers[i].Trim();
                if (string.IsNullOrWhiteSpace(racer))
                {
                    continue;
                }

                if (i < _laneRacers.Length)
                {
                    _laneRacers[i] = new RacerEntry(racer);
                }
                else
                {
                    _waitingRacers.Enqueue(new RacerEntry(racer));
                }
            }
        }

        private void RotateRacers()
        {
            RacerEntry rotatingOut = _laneRacers[^1];
            for (int i = _laneRacers.Length - 1; i > 0; i--)
            {
                _laneRacers[i] = _laneRacers[i - 1];
            }

            if (_waitingRacers.Count > 0)
            {
                _laneRacers[0] = _waitingRacers.Dequeue();
                if (!string.IsNullOrWhiteSpace(rotatingOut.Name))
                {
                    _waitingRacers.Enqueue(rotatingOut);
                }
            }
            else
            {
                _laneRacers[0] = rotatingOut;
            }
        }

        private void UpdateLaneLapCounts(IReadOnlyList<int> laneLapCounts)
        {
            for (int i = 0; i < _laneRacers.Length; i++)
            {
                _laneRacers[i].LapCount = i < laneLapCounts.Count ? Math.Max(0, laneLapCounts[i]) : 0;
            }
        }

        private void ClearRacers()
        {
            for (int i = 0; i < _laneRacers.Length; i++)
            {
                _laneRacers[i] = new RacerEntry(string.Empty);
            }
        }

        private sealed class RacerEntry
        {
            public RacerEntry(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public int LapCount { get; set; }
        }
    }
}
