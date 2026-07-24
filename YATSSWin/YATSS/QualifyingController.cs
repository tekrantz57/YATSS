namespace YATSS
{
    public enum QualifyingState
    {
        Inactive,
        Ready,
        Running,
        Complete
    }

    public sealed record QualifyingLapRecord(
        int LapNumber,
        int LapMilliseconds,
        int SessionElapsedMilliseconds);

    public sealed record QualifyingResult(
        string RacerName,
        int OriginalOrder,
        int? BestLapMilliseconds)
    {
        public int LaneIndex { get; init; } = -1;
        public int ConfiguredDurationSeconds { get; init; }
        public int ElapsedMilliseconds { get; init; }
        public IReadOnlyList<QualifyingLapRecord> Laps { get; init; } = Array.Empty<QualifyingLapRecord>();
    }

    public sealed class QualifyingController
    {
        private readonly object _gate = new();
        private readonly List<string> _racers = new();
        private readonly List<QualifyingResult> _results = new();
        private uint _startedAt;
        private int _currentIndex;
        private int _durationMilliseconds;

        public QualifyingState State { get; private set; } = QualifyingState.Inactive;
        public int LaneIndex { get; private set; }
        public int DurationSeconds => _durationMilliseconds / 1000;
        public int CurrentNumber => State == QualifyingState.Inactive ? 0 : Math.Min(_currentIndex + 1, _racers.Count);
        public int RacerCount => _racers.Count;
        public string CurrentRacer =>
            _currentIndex >= 0 && _currentIndex < _racers.Count ? _racers[_currentIndex] : string.Empty;

        public void Configure(IReadOnlyList<string> racers, int laneIndex, int durationSeconds)
        {
            lock (_gate)
            {
                _racers.Clear();
                _racers.AddRange(racers
                    .Select(racer => racer.Trim())
                    .Where(racer => !string.IsNullOrWhiteSpace(racer)));
                _results.Clear();
                _currentIndex = 0;
                LaneIndex = Math.Clamp(laneIndex, 0, LapProtocolParser.LaneCount - 1);
                _durationMilliseconds = Math.Clamp(durationSeconds, 1, 3600) * 1000;
                State = _racers.Count > 0 ? QualifyingState.Ready : QualifyingState.Inactive;
            }
        }

        public bool Start(uint controllerTimestamp)
        {
            lock (_gate)
            {
                if (State != QualifyingState.Ready)
                {
                    return false;
                }

                _startedAt = controllerTimestamp;
                State = QualifyingState.Running;
                return true;
            }
        }

        public bool IsExpired(uint controllerTimestamp)
        {
            lock (_gate)
            {
                return State == QualifyingState.Running &&
                    unchecked(controllerTimestamp - _startedAt) >= _durationMilliseconds;
            }
        }

        public TimeSpan GetRemaining(uint controllerTimestamp)
        {
            lock (_gate)
            {
                uint elapsed = State == QualifyingState.Running
                    ? unchecked(controllerTimestamp - _startedAt)
                    : 0;
                return TimeSpan.FromMilliseconds(Math.Max(0, _durationMilliseconds - elapsed));
            }
        }

        public bool CompleteCurrent(int? bestLapMilliseconds)
        {
            return CompleteCurrentCore(
                bestLapMilliseconds,
                Array.Empty<QualifyingLapRecord>(),
                _durationMilliseconds);
        }

        public bool InterruptCurrent()
        {
            lock (_gate)
            {
                if (State != QualifyingState.Running)
                {
                    return false;
                }

                State = QualifyingState.Ready;
                return true;
            }
        }

        public bool CompleteCurrent(IReadOnlyList<LaneLapRecord> laps, uint controllerTimestamp)
        {
            lock (_gate)
            {
                IReadOnlyList<QualifyingLapRecord> qualifyingLaps = laps
                    .Where(lap => lap.LapMilliseconds.HasValue)
                    .Select((lap, index) => new QualifyingLapRecord(
                        index + 1,
                        lap.LapMilliseconds!.Value,
                        (int)Math.Min(
                            unchecked(lap.TimestampMilliseconds - _startedAt),
                            int.MaxValue)))
                    .ToArray();
                int? bestLap = qualifyingLaps.Count == 0
                    ? null
                    : qualifyingLaps.Min(lap => lap.LapMilliseconds);
                int elapsedMilliseconds = (int)Math.Min(
                    unchecked(controllerTimestamp - _startedAt),
                    int.MaxValue);
                return CompleteCurrentCore(bestLap, qualifyingLaps, elapsedMilliseconds);
            }
        }

        private bool CompleteCurrentCore(
            int? bestLapMilliseconds,
            IReadOnlyList<QualifyingLapRecord> laps,
            int elapsedMilliseconds)
        {
            lock (_gate)
            {
                if (State != QualifyingState.Running)
                {
                    return false;
                }

                _results.Add(new QualifyingResult(
                    _racers[_currentIndex],
                    _currentIndex,
                    bestLapMilliseconds)
                {
                    LaneIndex = LaneIndex,
                    ConfiguredDurationSeconds = DurationSeconds,
                    ElapsedMilliseconds = Math.Max(0, elapsedMilliseconds),
                    Laps = laps.ToArray()
                });
                _currentIndex++;
                State = _currentIndex < _racers.Count
                    ? QualifyingState.Ready
                    : QualifyingState.Complete;
                return true;
            }
        }

        public IReadOnlyList<QualifyingResult> GetRankedResults()
        {
            lock (_gate)
            {
                return _results
                    .OrderBy(result => result.BestLapMilliseconds.HasValue ? 0 : 1)
                    .ThenBy(result => result.BestLapMilliseconds ?? int.MaxValue)
                    .ThenBy(result => result.OriginalOrder)
                    .ToArray();
            }
        }

        public static IReadOnlyList<string> BuildSeededRacers(
            IReadOnlyList<QualifyingResult> rankedResults,
            IReadOnlyList<int> selectedLaneByRank,
            int activeLaneCount)
        {
            int laneCount = Math.Clamp(activeLaneCount, 2, LapProtocolParser.LaneCount);
            string[] laneRacers = new string[laneCount];
            Array.Fill(laneRacers, string.Empty);
            int chooserCount = Math.Min(
                Math.Min(selectedLaneByRank.Count, rankedResults.Count),
                laneCount);
            for (int rank = 0; rank < chooserCount; rank++)
            {
                int laneIndex = selectedLaneByRank[rank];
                if (laneIndex < 0 || laneIndex >= laneCount ||
                    !string.IsNullOrWhiteSpace(laneRacers[laneIndex]))
                {
                    throw new ArgumentException("Qualifying lane choices must be unique active lanes.");
                }

                laneRacers[laneIndex] = rankedResults[rank].RacerName;
            }

            return laneRacers
                .Concat(rankedResults.Skip(chooserCount).Select(result => result.RacerName))
                .ToArray();
        }

        public void Reset()
        {
            lock (_gate)
            {
                _racers.Clear();
                _results.Clear();
                _currentIndex = 0;
                State = QualifyingState.Inactive;
            }
        }
    }
}
