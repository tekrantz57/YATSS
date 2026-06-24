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

    public sealed record HeatRaceLaneResult(
        int HeatNumber,
        int LaneIndex,
        string LaneName,
        string RacerName,
        int HeatLaps,
        int TotalLaps,
        int? BestLapMilliseconds);

    public sealed record HeatRaceRacerReport(
        string RacerName,
        int TotalLaps,
        IReadOnlyList<int> HeatLaps,
        IReadOnlyList<int?> BestLapByLaneMilliseconds);

    public sealed record HeatRaceReport(
        DateTime CreatedLocal,
        IReadOnlyList<string> LaneNames,
        IReadOnlyList<HeatRaceRacerReport> Racers,
        IReadOnlyList<HeatRaceLaneResult> LaneResults);

    public sealed class HeatRaceController
    {
        public const int TotalHeats = 8;
        private static readonly string[] LaneNameValues =
        {
            "Red",
            "White",
            "Green",
            "Orange",
            "Blue",
            "Yellow",
            "Purple",
            "Black"
        };
        private static readonly int[] InitialLaneOrder = { 0, 1, 2, 3, 4, 5, 6, 7 };
        private static readonly int[] RotationLaneOrder = { 0, 2, 4, 6, 7, 5, 3, 1 };
        public static IReadOnlyList<string> LaneNames => LaneNameValues;
        public static IReadOnlyList<int> InitialLaneIndexes => InitialLaneOrder;
        public static IReadOnlyList<int> RotationLaneIndexes => RotationLaneOrder;

        private readonly bool[] _laneSeenThisHeat = new bool[LapProtocolParser.LaneCount];
        private readonly RacerEntry[] _laneRacers = Enumerable.Range(0, LapProtocolParser.LaneCount)
            .Select(_ => new RacerEntry(string.Empty))
            .ToArray();
        private readonly Queue<RacerEntry> _waitingRacers = new();
        private readonly List<HeatRaceLaneResult> _laneResults = new();
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
                _laneResults.Clear();
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
                _laneResults.Clear();
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

                if (string.IsNullOrWhiteSpace(_laneRacers[edge.LaneIndex].Name))
                {
                    return new HeatRaceEdgeDecision(false, edge, false, false, "lane is unoccupied");
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

        public bool CanAdjustLapCounts
        {
            get
            {
                lock (_gate)
                {
                    return State == HeatRaceState.Paused || State == HeatRaceState.Complete;
                }
            }
        }

        public void RecordHeatResults(IReadOnlyList<int> laneLapCounts, IReadOnlyList<int?> laneBestLapMilliseconds)
        {
            lock (_gate)
            {
                if (HeatNumber <= 0)
                {
                    return;
                }

                _laneResults.RemoveAll(result => result.HeatNumber == HeatNumber);
                for (int i = 0; i < _laneRacers.Length; i++)
                {
                    string racerName = _laneRacers[i].Name;
                    if (string.IsNullOrWhiteSpace(racerName))
                    {
                        continue;
                    }

                    int totalLaps = i < laneLapCounts.Count ? Math.Max(0, laneLapCounts[i]) : 0;
                    int heatLaps = Math.Max(0, totalLaps - _laneRacers[i].LapCount);
                    int? bestLap = i < laneBestLapMilliseconds.Count ? laneBestLapMilliseconds[i] : null;
                    _laneResults.Add(new HeatRaceLaneResult(
                        HeatNumber,
                        i,
                        LaneNameValues[i],
                        racerName,
                        heatLaps,
                        totalLaps,
                        bestLap));
                }
            }
        }

        public HeatRaceReport CreateReport()
        {
            lock (_gate)
            {
                Dictionary<string, List<HeatRaceLaneResult>> byRacer = _laneResults
                    .GroupBy(result => result.RacerName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

                List<HeatRaceRacerReport> racers = new();
                foreach ((string racerName, List<HeatRaceLaneResult> results) in byRacer)
                {
                    int[] heatLaps = new int[TotalHeats];
                    int?[] bestByLane = new int?[LaneNameValues.Length];
                    foreach (HeatRaceLaneResult result in results)
                    {
                        if (result.HeatNumber >= 1 && result.HeatNumber <= TotalHeats)
                        {
                            heatLaps[result.HeatNumber - 1] += result.HeatLaps;
                        }

                        if (result.BestLapMilliseconds is int bestLap)
                        {
                            int laneIndex = result.LaneIndex;
                            bestByLane[laneIndex] = !bestByLane[laneIndex].HasValue
                                ? bestLap
                                : Math.Min(bestByLane[laneIndex]!.Value, bestLap);
                        }
                    }

                    racers.Add(new HeatRaceRacerReport(
                        racerName,
                        results.Max(result => result.TotalLaps),
                        heatLaps,
                        bestByLane));
                }

                return new HeatRaceReport(
                    DateTime.Now,
                    LaneNameValues,
                    racers
                        .OrderByDescending(racer => racer.TotalLaps)
                        .ThenBy(racer => racer.RacerName, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    _laneResults
                        .OrderBy(result => result.HeatNumber)
                        .ThenBy(result => result.LaneIndex)
                        .ToArray());
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

                if (i < InitialLaneIndexes.Count)
                {
                    _laneRacers[InitialLaneIndexes[i]] = new RacerEntry(racer);
                }
                else
                {
                    _waitingRacers.Enqueue(new RacerEntry(racer));
                }
            }
        }

        private void RotateRacers()
        {
            int whiteLaneIndex = RotationLaneIndexes[RotationLaneIndexes.Count - 1];
            RacerEntry rotatingOut = _laneRacers[whiteLaneIndex];
            for (int i = RotationLaneIndexes.Count - 1; i > 0; i--)
            {
                _laneRacers[RotationLaneIndexes[i]] = _laneRacers[RotationLaneIndexes[i - 1]];
            }

            if (_waitingRacers.Count > 0)
            {
                _laneRacers[RotationLaneIndexes[0]] = _waitingRacers.Dequeue();
                if (!string.IsNullOrWhiteSpace(rotatingOut.Name))
                {
                    _waitingRacers.Enqueue(rotatingOut);
                }
            }
            else
            {
                _laneRacers[RotationLaneIndexes[0]] = rotatingOut;
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
