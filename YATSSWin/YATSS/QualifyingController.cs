namespace YATSS
{
    public enum QualifyingState
    {
        Inactive,
        Ready,
        Running,
        Complete
    }

    public sealed record QualifyingResult(
        string RacerName,
        int OriginalOrder,
        int? BestLapMilliseconds);

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
            lock (_gate)
            {
                if (State != QualifyingState.Running)
                {
                    return false;
                }

                _results.Add(new QualifyingResult(
                    _racers[_currentIndex],
                    _currentIndex,
                    bestLapMilliseconds));
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
