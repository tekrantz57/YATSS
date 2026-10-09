namespace YATSS
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
        int? FirstLapMilliseconds,
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

    public sealed record HeatRaceLapRecord(
        int HeatNumber,
        int LaneIndex,
        string LaneName,
        string RacerName,
        int LapNumberInHeat,
        int RacerTotalLapNumber,
        int? LapMilliseconds,
        long RaceElapsedMilliseconds,
        bool FastestLapEligible);

    public sealed record HeatRaceManualAdjustment(
        int HeatNumber,
        int LaneIndex,
        string LaneName,
        string RacerName,
        int Delta,
        int ResultingTotalLaps,
        long RaceElapsedMilliseconds,
        DateTimeOffset RecordedAt);

    public sealed record HeatRaceReport(
        DateTime CreatedLocal,
        string RaceName,
        int HeatLengthMinutes,
        int BetweenHeatsSeconds,
        double TrackLengthFeet,
        int TotalHeats,
        IReadOnlyList<string> LaneNames,
        IReadOnlyList<int> LaneColorArgb,
        IReadOnlyList<QualifyingResult> QualifyingResults,
        IReadOnlyList<HeatRaceRacerReport> Racers,
        IReadOnlyList<HeatRaceLaneResult> LaneResults,
        IReadOnlyList<HeatRaceLapRecord> Laps,
        IReadOnlyList<HeatRaceManualAdjustment> ManualAdjustments,
        string Notes)
    {
        public RaceFormat Format { get; init; }
        public IReadOnlyList<DistanceStanding> DistanceStandings { get; init; } = Array.Empty<DistanceStanding>();
        public IReadOnlyDictionary<int, DistanceApproval> FinalDistanceApprovals { get; init; } = new Dictionary<int, DistanceApproval>();
        public DateTimeOffset? FinalTieOrderRecordedAt { get; init; }
    }

    public sealed partial class HeatRaceController
    {
        public const int MaximumHeatLengthMinutes = 24 * 60;

        private static readonly LaneConfiguration[] DefaultLaneConfigurations =
            LaneConfiguration.CreateDefaults().ToArray();
        private static readonly string[] LaneNameValues =
            DefaultLaneConfigurations.Select(lane => lane.Name).ToArray();
        private static readonly int[] InitialLaneOrder = { 0, 1, 2, 3, 4, 5, 6, 7 };
        private static readonly int[] RotationLaneOrder = { 0, 2, 4, 6, 7, 5, 3, 1 };
        public static IReadOnlyList<string> LaneNames => LaneNameValues;
        public static IReadOnlyList<int> GetInitialLaneIndexes(int activeLaneCount) =>
            InitialLaneOrder.Where(lane => lane < Math.Clamp(activeLaneCount, 2, LapProtocolParser.LaneCount)).ToArray();
        public static IReadOnlyList<int> GetRotationLaneIndexes(int activeLaneCount) =>
            RotationLaneOrder.Where(lane => lane < Math.Clamp(activeLaneCount, 2, LapProtocolParser.LaneCount)).ToArray();

        private readonly bool[] _laneSeenThisHeat = new bool[LapProtocolParser.LaneCount];
        private readonly RacerEntry[] _laneRacers = Enumerable.Range(0, LapProtocolParser.LaneCount)
            .Select(_ => new RacerEntry(string.Empty))
            .ToArray();
        private readonly Queue<RacerEntry> _waitingRacers = new();
        private readonly List<HeatRaceLaneResult> _laneResults = new();
        private readonly List<HeatRaceLapRecord> _laps = new();
        private readonly List<HeatRaceManualAdjustment> _manualAdjustments = new();
        private readonly object _gate = new();
        private long _heatLengthMilliseconds;
        private int _betweenHeatsSeconds;
        private long _activeMillisecondsBeforeRun;
        private long _raceTimestampBase;
        private uint _runStartedAt;
        private bool _hasRunStartedAt;
        private bool _isFirstHeat = true;
        private int[] _initialLaneIndexes = InitialLaneOrder;
        private int[] _rotationLaneIndexes = RotationLaneOrder;
        private string[] _laneNames = LaneNameValues.ToArray();
        private int[] _laneColorArgb = DefaultLaneConfigurations
            .Select(lane => lane.ColorArgb)
            .ToArray();
        private string _raceName = string.Empty;
        private double _trackLengthFeet = LapRaceOptions.Default.TrackLengthFeet;
        private IReadOnlyList<QualifyingResult> _qualifyingResults = Array.Empty<QualifyingResult>();
        private IReadOnlyList<IReadOnlyList<string>> _groups = Array.Empty<IReadOnlyList<string>>();
        private readonly Dictionary<int, DistanceApproval> _finalApprovals = new();
        private readonly Dictionary<int, int> _directorTieOrder = new();
        private DateTimeOffset? _finalTieOrderRecordedAt;
        private int _recoveryHistoryVersion;
        public RaceFormat Format { get; private set; }
        public int GroupNumber => _groups.Count == 0 ? 1 : (HeatNumber - 1) / _initialLaneIndexes.Length + 1;
        public int GroupCount => Math.Max(1, _groups.Count);
        public bool IsGroupEnd => _groups.Count > 0 && HeatNumber % _initialLaneIndexes.Length == 0;
        public bool NextHeatStartsGroup => IsGroupEnd && HasMoreHeats;
        public bool StartsAtLine => Format == RaceFormat.CombinedDistance && _isFirstHeat;
        public uint TimingBaseTimestamp => (uint)_raceTimestampBase;
        public bool FinalDistancesConfirmed => _qualifyingResults.Count > 0 && _finalApprovals.Count == _qualifyingResults.Count;

        public HeatRaceState State { get; private set; } = HeatRaceState.Practice;
        public int HeatNumber { get; private set; }
        public int TotalHeats { get; private set; } = LapProtocolParser.LaneCount;
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

        public void Configure(
            int heatLengthMinutes,
            int betweenHeatsSeconds,
            IReadOnlyList<string> racers,
            int activeLaneCount = LapProtocolParser.LaneCount,
            IReadOnlyList<LaneConfiguration>? laneConfigurations = null,
            string raceName = "",
            double trackLengthFeet = 155.0,
            IReadOnlyList<QualifyingResult>? qualifyingResults = null,
            RaceFormat format = RaceFormat.HeatRace,
            IReadOnlyList<IReadOnlyList<string>>? groups = null)
        {
            lock (_gate)
            {
                int laneCount = Math.Clamp(activeLaneCount, 2, LapProtocolParser.LaneCount);
                int racerCount = racers.Count(racer => !string.IsNullOrWhiteSpace(racer));
                TotalHeats = Math.Max(laneCount, racerCount);
                _initialLaneIndexes = GetInitialLaneIndexes(laneCount).ToArray();
                _rotationLaneIndexes = GetRotationLaneIndexes(laneCount).ToArray();
                _laneNames = Enumerable.Range(0, LapProtocolParser.LaneCount)
                    .Select(lane =>
                        laneConfigurations != null &&
                        lane < laneConfigurations.Count &&
                        !string.IsNullOrWhiteSpace(laneConfigurations[lane].Name)
                            ? laneConfigurations[lane].Name.Trim()
                            : LaneNameValues[lane])
                    .ToArray();
                _laneColorArgb = Enumerable.Range(0, LapProtocolParser.LaneCount)
                    .Select(lane =>
                        laneConfigurations != null && lane < laneConfigurations.Count
                            ? laneConfigurations[lane].ColorArgb
                            : DefaultLaneConfigurations[lane].ColorArgb)
                    .ToArray();
                _raceName = raceName.Trim();
                _trackLengthFeet = Math.Clamp(trackLengthFeet, 1.0, 10000.0);
                _qualifyingResults = qualifyingResults?.ToArray() ?? Array.Empty<QualifyingResult>();
                Format = format;
                _groups = groups?.Select(group => (IReadOnlyList<string>)group.ToArray()).ToArray()
                    ?? Array.Empty<IReadOnlyList<string>>();
                if (format == RaceFormat.CombinedDistance && _groups.Count > 0)
                {
                    if (_groups.Any(group => group.Count != laneCount) ||
                        _groups.Count != (_qualifyingResults.Count + laneCount - 1) / laneCount ||
                        _qualifyingResults.Select(result => result.OriginalOrder).Distinct().Count() != _qualifyingResults.Count ||
                        _qualifyingResults.Any(result => result.Distance == null) ||
                        _groups.SelectMany(group => group).Where(name => !string.IsNullOrWhiteSpace(name)).Count() != _qualifyingResults.Count ||
                        !_groups.SelectMany(group => group).Where(name => !string.IsNullOrWhiteSpace(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                            .SequenceEqual(_qualifyingResults.Select(result => result.RacerName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase) ||
                        _qualifyingResults.Select(result => result.RacerName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _qualifyingResults.Count)
                        throw new ArgumentException("Combined Distance requires approved qualifying and complete group lane assignments.");
                    for (int group = 0; group < _groups.Count; group++)
                    {
                        string[] members = _groups[group].Where(name => !string.IsNullOrWhiteSpace(name)).OrderBy(name => name).ToArray();
                        string[] rankedMembers = _qualifyingResults.Skip(group * laneCount).Take(laneCount)
                            .Select(result => result.RacerName).OrderBy(name => name).ToArray();
                        if (!members.SequenceEqual(rankedMembers)) throw new ArgumentException("Groups must use consecutive qualifying ranks.");
                    }
                    TotalHeats = laneCount * _groups.Count;
                }
                _finalApprovals.Clear();
                _recoveryHistoryVersion++;
                _directorTieOrder.Clear();
                _finalTieOrderRecordedAt = null;
                _heatLengthMilliseconds = Math.Clamp(
                    heatLengthMinutes,
                    1,
                    MaximumHeatLengthMinutes) * 60000L;
                _betweenHeatsSeconds = Math.Clamp(betweenHeatsSeconds, 0, 300);
                _activeMillisecondsBeforeRun = 0;
                _raceTimestampBase = 0;
                _hasRunStartedAt = false;
                HeatNumber = 1;
                _isFirstHeat = true;
                _laneResults.Clear();
                _laps.Clear();
                _manualAdjustments.Clear();
                SetInitialRacers(_groups.Count > 0 ? _groups[0] : racers);
                Array.Fill(_laneSeenThisHeat, false);
                State = HeatRaceState.Ready;
            }
        }

        public void SetPracticeMode()
        {
            lock (_gate)
            {
                State = HeatRaceState.Practice;
                _recoveryHistoryVersion++;
                Format = RaceFormat.HeatRace;
                _groups = Array.Empty<IReadOnlyList<string>>();
                _activeMillisecondsBeforeRun = 0;
                _raceTimestampBase = 0;
                _hasRunStartedAt = false;
                HeatNumber = 0;
                _laneResults.Clear();
                _laps.Clear();
                _manualAdjustments.Clear();
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
                if (Format == RaceFormat.CombinedDistance && _groups.Count == 0) return false;

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

                if (IsGroupEnd && Format == RaceFormat.CombinedDistance && _laneRacers.Where(racer => !string.IsNullOrWhiteSpace(racer.Name))
                    .Any(racer => !_finalApprovals.ContainsKey(GetRacerId(racer.Name)))) return false;
                UpdateLaneLapCounts(laneLapCounts);
                if (IsGroupEnd)
                {
                    SetInitialRacers(_groups[GroupNumber]);
                }
                else RotateRacers();
                HeatNumber++;
                _activeMillisecondsBeforeRun = 0;
                _hasRunStartedAt = false;
                _isFirstHeat = _groups.Count > 0 && (HeatNumber - 1) % _initialLaneIndexes.Length == 0;
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

        public byte GetOccupiedLaneMask()
        {
            lock (_gate)
            {
                byte mask = 0;
                for (int lane = 0; lane < _laneRacers.Length; lane++)
                {
                    if (!string.IsNullOrWhiteSpace(_laneRacers[lane].Name))
                    {
                        mask |= (byte)(1 << lane);
                    }
                }

                return mask;
            }
        }

        public HeatRaceEdgeDecision PrepareEdge(LapEdge edge)
        {
            lock (_gate)
            {
                if (State != HeatRaceState.Running)
                {
                    return new HeatRaceEdgeDecision(false, edge, false, false, null, "heat is not running");
                }

                if (string.IsNullOrWhiteSpace(_laneRacers[edge.LaneIndex].Name))
                {
                    return new HeatRaceEdgeDecision(false, edge, false, false, null, "lane is unoccupied");
                }

                bool isFirstLaneEdge = !_laneSeenThisHeat[edge.LaneIndex];
                _laneSeenThisHeat[edge.LaneIndex] = true;

                bool countFirstEdgeAsLap = isFirstLaneEdge && !_isFirstHeat;
                bool fastestLapEligible = !isFirstLaneEdge || _isFirstHeat;
                long elapsedHeatMilliseconds = GetElapsedMillisecondsCore(edge.TimestampMillis);
                uint adjustedTimestamp = (uint)(_raceTimestampBase + elapsedHeatMilliseconds);
                LapEdge adjustedEdge = edge with { TimestampMillis = adjustedTimestamp };
                int? firstLapMilliseconds = countFirstEdgeAsLap
                    ? (int)Math.Min(elapsedHeatMilliseconds, int.MaxValue)
                    : null;

                return new HeatRaceEdgeDecision(
                    true,
                    adjustedEdge,
                    countFirstEdgeAsLap,
                    fastestLapEligible,
                    firstLapMilliseconds,
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
                RecordHeatResultsCore(laneLapCounts, laneBestLapMilliseconds, null);
            }
        }

        public void RecordHeatResults(IReadOnlyList<LapRaceLaneSnapshot> laneSnapshots)
        {
            lock (_gate)
            {
                RecordHeatResultsCore(
                    laneSnapshots.Select(snapshot => snapshot.TotalLapCount).ToArray(),
                    laneSnapshots.Select(snapshot => snapshot.BestLapMilliseconds).ToArray(),
                    laneSnapshots);
            }
        }

        public void RecordManualLapAdjustment(int laneIndex, int delta, int resultingTotalLaps)
        {
            lock (_gate)
            {
                if (HeatNumber <= 0 || laneIndex < 0 || laneIndex >= _laneRacers.Length || delta == 0)
                {
                    return;
                }

                string racerName = _laneRacers[laneIndex].Name;
                if (string.IsNullOrWhiteSpace(racerName))
                {
                    return;
                }

                _manualAdjustments.Add(new HeatRaceManualAdjustment(
                    HeatNumber,
                    laneIndex,
                    _laneNames[laneIndex],
                    racerName,
                    delta,
                    Math.Max(0, resultingTotalLaps),
                    State == HeatRaceState.Complete
                        ? _raceTimestampBase
                        : _raceTimestampBase + _activeMillisecondsBeforeRun,
                    DateTimeOffset.Now));
            }
        }

        private void RecordHeatResultsCore(
            IReadOnlyList<int> laneLapCounts,
            IReadOnlyList<int?> laneBestLapMilliseconds,
            IReadOnlyList<LapRaceLaneSnapshot>? laneSnapshots)
        {
            if (HeatNumber <= 0)
            {
                return;
            }

            _recoveryHistoryVersion++;

            _laneResults.RemoveAll(result => result.HeatNumber == HeatNumber);
            _laps.RemoveAll(lap => lap.HeatNumber == HeatNumber);
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
                    _laneNames[i],
                    racerName,
                    heatLaps,
                    totalLaps,
                    bestLap));

                LapRaceLaneSnapshot? snapshot = laneSnapshots?.FirstOrDefault(item => item.LaneIndex == i);
                if (snapshot == null)
                {
                    continue;
                }

                for (int lapIndex = 0; lapIndex < snapshot.Laps.Count; lapIndex++)
                {
                    LaneLapRecord lap = snapshot.Laps[lapIndex];
                    _laps.Add(new HeatRaceLapRecord(
                        HeatNumber,
                        i,
                        _laneNames[i],
                        racerName,
                        lapIndex + 1,
                        _laneRacers[i].LapCount + lapIndex + 1,
                        lap.LapMilliseconds,
                        lap.TimestampMilliseconds,
                        lap.FastestLapEligible));
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
                        results.OrderBy(result => result.HeatNumber).Last().TotalLaps,
                        heatLaps,
                        bestByLane));
                }

                HeatRaceReport report = new HeatRaceReport(
                    DateTime.Now,
                    _raceName,
                    HeatLengthMinutes,
                    BetweenHeatsSeconds,
                    _trackLengthFeet,
                    TotalHeats,
                    _laneNames.Take(_initialLaneIndexes.Length).ToArray(),
                    _laneColorArgb.Take(_initialLaneIndexes.Length).ToArray(),
                    _qualifyingResults,
                    racers
                        .OrderByDescending(racer => racer.TotalLaps)
                        .ThenBy(racer => racer.RacerName, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    _laneResults
                        .OrderBy(result => result.HeatNumber)
                        .ThenBy(result => result.LaneIndex)
                        .ToArray(),
                    _laps
                        .OrderBy(lap => lap.HeatNumber)
                        .ThenBy(lap => lap.LaneIndex)
                        .ThenBy(lap => lap.LapNumberInHeat)
                        .ToArray(),
                    _manualAdjustments.ToArray(),
                    "Manual lap adjustments made during stopped time are reflected in totals.");
                if (Format == RaceFormat.CombinedDistance)
                {
                    if (!FinalDistancesConfirmed) throw new InvalidOperationException("Final distances have not all been confirmed.");
                    IReadOnlyList<DistanceStanding> standings = GetDistanceStandings(0);
                    if (standings.GroupBy(row => (row.CombinedHundredths, row.BestLapMilliseconds))
                        .Any(tie => tie.Count() > 1 && tie.Any(row => row.DirectorTieOrder == null)))
                        throw new InvalidOperationException("Exact final ties require a director order.");
                    Dictionary<string, HeatRaceRacerReport> byName = report.Racers.ToDictionary(racer => racer.RacerName);
                    report = report with
                    {
                        Format = Format,
                        DistanceStandings = standings,
                        FinalDistanceApprovals = new Dictionary<int, DistanceApproval>(_finalApprovals),
                        FinalTieOrderRecordedAt = _finalTieOrderRecordedAt,
                        Racers = standings.Select(row => byName[row.RacerName]).ToArray(),
                        Notes = "Combined Distance: approved qualifying credit + counted race laps + approved final fraction. All distances are in laps."
                    };
                }
                return report;
            }
        }

        private long GetElapsedMillisecondsCore(uint controllerTimestamp)
        {
            if (!_hasRunStartedAt)
            {
                return _activeMillisecondsBeforeRun;
            }

            uint runElapsed = unchecked(controllerTimestamp - _runStartedAt);
            if (runElapsed > int.MaxValue)
            {
                runElapsed = 0;
            }

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

                if (i < _initialLaneIndexes.Length)
                {
                    _laneRacers[_initialLaneIndexes[i]] = new RacerEntry(racer);
                }
                else
                {
                    _waitingRacers.Enqueue(new RacerEntry(racer));
                }
            }
        }

        private void RotateRacers()
        {
            int lastLaneIndex = _rotationLaneIndexes[_rotationLaneIndexes.Length - 1];
            RacerEntry rotatingOut = _laneRacers[lastLaneIndex];
            for (int i = _rotationLaneIndexes.Length - 1; i > 0; i--)
            {
                _laneRacers[_rotationLaneIndexes[i]] = _laneRacers[_rotationLaneIndexes[i - 1]];
            }

            if (_waitingRacers.Count > 0)
            {
                _laneRacers[_rotationLaneIndexes[0]] = _waitingRacers.Dequeue();
                if (!string.IsNullOrWhiteSpace(rotatingOut.Name))
                {
                    _waitingRacers.Enqueue(rotatingOut);
                }
            }
            else
            {
                _laneRacers[_rotationLaneIndexes[0]] = rotatingOut;
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

        private int GetRacerId(string name) => _qualifyingResults.Single(result => result.RacerName == name).OriginalOrder;

        public IReadOnlyList<(int RacerId, string RacerName, int Laps, int? Estimate)> GetFinalDistanceCandidates()
        {
            lock (_gate)
            {
                if (Format != RaceFormat.CombinedDistance || State != HeatRaceState.Complete || !IsGroupEnd)
                    return Array.Empty<(int, string, int, int?)>();
                return _laneRacers.Where(racer => !string.IsNullOrWhiteSpace(racer.Name) && !_finalApprovals.ContainsKey(GetRacerId(racer.Name))).Select(racer =>
                {
                    HeatRaceLaneResult last = _laneResults.Last(result => result.RacerName == racer.Name);
                    HeatRaceLapRecord[] laps = _laps.Where(lap => lap.RacerName == racer.Name && lap.HeatNumber == HeatNumber).ToArray();
                    int? estimate = laps.Length == 0 ? null : DistanceScoring.EstimatePartial(
                        _raceTimestampBase - laps[^1].RaceElapsedMilliseconds,
                        laps.Where(lap => lap.FastestLapEligible && lap.LapMilliseconds.HasValue).Select(lap => lap.LapMilliseconds!.Value));
                    return (GetRacerId(racer.Name), racer.Name, last.TotalLaps, estimate);
                }).ToArray();
            }
        }

        public void ApproveFinalDistance(int racerId, int partialHundredths, string reason)
        {
            lock (_gate)
            {
                var candidate = GetFinalDistanceCandidates().Single(item => item.RacerId == racerId);
                if (_finalApprovals.ContainsKey(racerId)) throw new InvalidOperationException("Distance is already confirmed.");
                _finalApprovals.Add(racerId, DistanceScoring.Approve(candidate.Laps, candidate.Estimate, partialHundredths, reason));
            }
        }

        public void SetFinalTieOrder(IReadOnlyList<int> ids)
        {
            lock (_gate)
            {
                if (ids.Count != _qualifyingResults.Count || ids.Distinct().Count() != ids.Count ||
                    ids.Any(id => !_qualifyingResults.Any(result => result.OriginalOrder == id)))
                    throw new ArgumentException("Tie order must include each racer once.");
                _directorTieOrder.Clear();
                for (int index = 0; index < ids.Count; index++) _directorTieOrder.Add(ids[index], index);
                _finalTieOrderRecordedAt = DateTimeOffset.Now;
            }
        }

        public IReadOnlyList<DistanceStanding> GetDistanceStandings(uint timestamp, IReadOnlyList<LapRaceLaneSnapshot>? current = null)
        {
            lock (_gate)
            {
                List<DistanceStanding> rows = new();
                foreach (QualifyingResult qualifier in _qualifyingResults)
                {
                    int id = qualifier.OriginalOrder;
                    HeatRaceLaneResult[] results = _laneResults.Where(result => result.RacerName == qualifier.RacerName).ToArray();
                    int lane = Array.FindIndex(_laneRacers, racer => racer.Name == qualifier.RacerName);
                    bool active = lane >= 0 && State != HeatRaceState.Complete;
                    LapRaceLaneSnapshot? live = active ? current?.FirstOrDefault(snapshot => snapshot.LaneIndex == lane) : null;
                    int laps = live?.TotalLapCount ?? results.LastOrDefault()?.TotalLaps ?? 0;
                    int[] valid = _laps.Where(lap => lap.RacerName == qualifier.RacerName && lap.FastestLapEligible && lap.LapMilliseconds.HasValue)
                        .Select(lap => lap.LapMilliseconds!.Value)
                        .Concat(live?.Laps.Where(lap => lap.FastestLapEligible && lap.LapMilliseconds.HasValue).Select(lap => lap.LapMilliseconds!.Value) ?? Enumerable.Empty<int>()).ToArray();
                    long qualifying = qualifier.Distance?.TotalHundredths ?? 0;
                    _finalApprovals.TryGetValue(id, out DistanceApproval? final);
                    long combined = qualifying + laps * 100L + (final?.ApprovedPartialHundredths ?? 0);
                    long activeTime = results.Length * _heatLengthMilliseconds + (active ? GetElapsedMillisecondsCore(timestamp) : 0);
                    long remaining = Math.Max(0, _initialLaneIndexes.Length * _heatLengthMilliseconds - activeTime);
                    long? projected = final != null ? combined : valid.Length == 0 || activeTime <= 0 ? null :
                        qualifying + (long)Math.Floor(laps * 100d * (activeTime + remaining) / activeTime);
                    int group = _groups.Select((members, index) => (members, index)).FirstOrDefault(item => item.members.Contains(qualifier.RacerName)).index + 1;
                    rows.Add(new(id, qualifier.RacerName, group, lane >= 0 && final == null ? _laneNames[lane] : string.Empty,
                        qualifying, laps, final?.ApprovedPartialHundredths, combined, projected,
                        valid.Length > 0 ? valid.Min() : null, _directorTieOrder.TryGetValue(id, out int order) ? order : null));
                }
                return DistanceScoring.Rank(rows);
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
