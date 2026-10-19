namespace YATSS;

public sealed record RecoveryRacer(string Name, int LapCount);
public sealed record HeatRecovery(
    HeatRaceState State, int HeatNumber, int TotalHeats, long ElapsedMilliseconds,
    long TimestampBase, bool FirstHeat, int HeatLengthSeconds, int BetweenHeatsSeconds,
    int ActiveLaneCount, string RaceName, double TrackLengthFeet, RaceFormat Format,
    LaneConfiguration[] LaneConfigurations, QualifyingResult[] Qualifiers, string[][] Groups,
    RecoveryRacer[] Lanes, RecoveryRacer[] Waiting, bool[] Seen,
    HeatRaceLaneResult[] Results, HeatRaceLapRecord[]? Laps,
    HeatRaceManualAdjustment[] Adjustments, Dictionary<int, DistanceApproval> Approvals,
    Dictionary<int, int> TieOrder, DateTimeOffset? TieRecordedAt, int HistoryVersion)
{
    public HeatRaceRerun[]? Reruns { get; init; } = Array.Empty<HeatRaceRerun>();
    public HeatRaceScheduling Scheduling { get; init; }
    public int LapTarget { get; init; }
    public LapFinishOutcome? LapFinish { get; init; }
}
public sealed record QualifyingRecovery(QualifyingState State, string[] Racers,
    QualifyingResult[] Results, int CurrentIndex, int LaneIndex, int DurationSeconds);
public sealed record LaneRecovery(int Epoch, int StartIndex, LapRaceLaneSnapshot Snapshot,
    uint? AcceptedTimestamp, uint? RawTimestamp, uint? Sequence, int MissedFrames, bool CountAfterInterruption);
public sealed record RaceRecoveryFrame(int SchemaVersion, Guid EventId, DateTimeOffset SavedAt,
    string Reason, HeatRecovery Heat, QualifyingRecovery Qualifying,
    LapRaceOptions Options, LaneRecovery[] Lanes, bool Demo, bool ReportWritten,
    string[] ConfiguredRacers, int SensorDebounceMilliseconds)
{
    public RaceExportPlan? ExportPlan { get; init; }
}

public sealed partial class HeatRaceController
{
    public HeatRecovery CaptureRecovery(uint timestamp, int? savedHistoryVersion = null)
    {
        lock (_gate)
        {
            uint runElapsed = _hasRunStartedAt ? unchecked(timestamp - _runStartedAt) : 0;
            // A confirmed frame can precede the estimated clock used to start.
            if (runElapsed > int.MaxValue) runElapsed = 0;
            long elapsed = Format == RaceFormat.LapFinish ? _activeMillisecondsBeforeRun + runElapsed :
                State == HeatRaceState.Complete ? _heatLengthMilliseconds :
                Math.Clamp(_activeMillisecondsBeforeRun + runElapsed, 0, _heatLengthMilliseconds);
            return new(State, HeatNumber, TotalHeats, elapsed, _raceTimestampBase, _isFirstHeat,
                HeatLengthSeconds, _betweenHeatsSeconds, _initialLaneIndexes.Length, _raceName,
                _trackLengthFeet, Format,
                _laneNames.Select((name, index) => new LaneConfiguration(name, _laneColorArgb[index])).ToArray(),
                _qualifyingResults.ToArray(), _groups.Select(group => group.ToArray()).ToArray(),
                _laneRacers.Select(racer => new RecoveryRacer(racer.Name, racer.LapCount)).ToArray(),
                _waitingRacers.Select(racer => new RecoveryRacer(racer.Name, racer.LapCount)).ToArray(),
                _laneSeenThisHeat.ToArray(), _laneResults.ToArray(),
                savedHistoryVersion == _recoveryHistoryVersion ? null : _laps.ToArray(),
                _manualAdjustments.ToArray(), new(_finalApprovals), new(_directorTieOrder),
                _finalTieOrderRecordedAt, _recoveryHistoryVersion)
                { Reruns = savedHistoryVersion == _recoveryHistoryVersion ? null : _reruns.ToArray(), Scheduling = Scheduling,
                    LapTarget = LapTarget, LapFinish = LapFinish };
        }
    }

    public void RestoreRecovery(HeatRecovery saved)
    {
        if (saved.ActiveLaneCount is < 2 or > 8 || saved.Lanes.Length != 8 || saved.Seen.Length != 8 ||
            saved.LaneConfigurations.Length != 8 || saved.HeatNumber < 1 || saved.HeatNumber > saved.TotalHeats ||
            saved.HeatLengthSeconds is < 1 or > MaximumHeatLengthSeconds ||
            saved.ElapsedMilliseconds < 0 || (saved.Format != RaceFormat.LapFinish && saved.ElapsedMilliseconds > saved.HeatLengthSeconds * 1000L) ||
            saved.State == HeatRaceState.Practice || saved.Laps == null)
            throw new InvalidDataException("Invalid saved heat state.");
        if (saved.Format == RaceFormat.LapFinish && (saved.TotalHeats != 1 || saved.HeatNumber != 1 || saved.Waiting.Length != 0 ||
            (saved.State == HeatRaceState.Complete) != (saved.LapFinish != null) ||
            (saved.LapFinish is { } finish && (finish.WinnerLaneIndex < 0 || finish.WinnerLaneIndex >= saved.ActiveLaneCount ||
                string.IsNullOrWhiteSpace(saved.Lanes[finish.WinnerLaneIndex].Name) || finish.ElapsedMilliseconds != saved.ElapsedMilliseconds))))
            throw new InvalidDataException("Invalid saved Lap Finish outcome.");
        lock (_gate)
        {
            Configure(saved.HeatLengthSeconds, saved.BetweenHeatsSeconds,
                saved.Lanes.Select(racer => racer.Name).Concat(saved.Waiting.Select(racer => racer.Name)).ToArray(),
                saved.ActiveLaneCount, saved.LaneConfigurations, saved.RaceName, saved.TrackLengthFeet,
                saved.Qualifiers, saved.Format, saved.Groups.Length == 0 ? null : saved.Groups, saved.Scheduling, saved.LapTarget);
            LapFinish = saved.LapFinish;
            HeatNumber = saved.HeatNumber;
            TotalHeats = saved.TotalHeats;
            _activeMillisecondsBeforeRun = saved.ElapsedMilliseconds;
            _raceTimestampBase = saved.TimestampBase;
            _isFirstHeat = saved.FirstHeat;
            _hasRunStartedAt = false;
            State = saved.State == HeatRaceState.Running ? HeatRaceState.Paused : saved.State;
            for (int lane = 0; lane < 8; lane++)
            {
                _laneRacers[lane] = new RacerEntry(saved.Lanes[lane].Name) { LapCount = saved.Lanes[lane].LapCount };
                _laneSeenThisHeat[lane] = saved.Seen[lane];
            }
            _waitingRacers.Clear();
            foreach (RecoveryRacer racer in saved.Waiting)
                _waitingRacers.Enqueue(new RacerEntry(racer.Name) { LapCount = racer.LapCount });
            _laneResults.Clear(); _laneResults.AddRange(saved.Results);
            _laps.Clear(); _laps.AddRange(saved.Laps);
            _manualAdjustments.Clear(); _manualAdjustments.AddRange(saved.Adjustments);
            _finalApprovals.Clear(); foreach (var pair in saved.Approvals) _finalApprovals.Add(pair.Key, pair.Value);
            _directorTieOrder.Clear(); foreach (var pair in saved.TieOrder) _directorTieOrder.Add(pair.Key, pair.Value);
            _finalTieOrderRecordedAt = saved.TieRecordedAt;
            _recoveryHistoryVersion = saved.HistoryVersion;
            _reruns.Clear(); _reruns.AddRange(saved.Reruns ?? throw new InvalidDataException("Missing rerun history."));
        }
    }
}

public sealed partial class QualifyingController
{
    public QualifyingRecovery CaptureRecovery()
    {
        lock (_gate) return new(State, _racers.ToArray(), _results.ToArray(), _currentIndex, LaneIndex, DurationSeconds);
    }

    public void RestoreRecovery(QualifyingRecovery saved)
    {
        if (saved.State != QualifyingState.Inactive && (saved.DurationSeconds is < 1 or > 3600 ||
            saved.LaneIndex is < 0 or > 7 || saved.CurrentIndex < 0 || saved.CurrentIndex > saved.Racers.Length ||
            saved.Results.Length != saved.CurrentIndex))
            throw new InvalidDataException("Invalid saved qualifying state.");
        lock (_gate)
        {
            Configure(saved.Racers, saved.LaneIndex, saved.DurationSeconds);
            _results.AddRange(saved.Results);
            _currentIndex = saved.CurrentIndex;
            State = saved.State is QualifyingState.Running or QualifyingState.Paused ? QualifyingState.Ready : saved.State;
        }
    }
}

public sealed partial class LapRace
{
    public LaneRecovery[] CaptureRecovery(IReadOnlyList<LaneRecovery>? previous = null)
    {
        lock (_gate)
        {
            return _lanes.Select((lane, index) =>
            {
                LaneRecovery? prior = previous?.FirstOrDefault(item => item.Snapshot.LaneIndex == index);
                int start = prior?.Epoch == lane.RecoveryEpoch ?
                    prior.StartIndex + prior.Snapshot.Laps.Count : 0;
                if (start > lane.Stats.RecordedLapCount) start = 0;
                return new LaneRecovery(lane.RecoveryEpoch, start,
                    new(index, lane.Stats.getCount(), lane.Stats.ManualLapAdjustment,
                        lane.Stats.best_time == int.MaxValue ? null : lane.Stats.best_time,
                        lane.Stats.GetLapRecordsSince(start)),
                    lane.LastAcceptedTimestamp, lane.LastRawTimestamp, lane.LastSequence,
                    lane.MissedFrames, lane.CountAfterInterruption);
            }).ToArray();
        }
    }

    public void RestoreRecovery(LapRaceOptions options, IReadOnlyList<LaneRecovery> saved)
    {
        if (saved.Count != 8 || !saved.Select(item => item.Snapshot.LaneIndex).Order().SequenceEqual(Enumerable.Range(0, 8)) ||
            saved.Any(item => item.StartIndex != 0)) throw new InvalidDataException("Invalid saved lap history.");
        lock (_gate)
        {
            _options = options;
            foreach (LaneRecovery item in saved)
            {
                LaneRuntime lane = _lanes[item.Snapshot.LaneIndex];
                lane.Reset(item.Snapshot.LaneIndex);
                lane.Stats.Restore(item.Snapshot);
                lane.RecoveryEpoch = item.Epoch;
                lane.LastAcceptedTimestamp = item.AcceptedTimestamp;
                lane.CountAfterInterruption = item.CountAfterInterruption;
                lane.MissedFrames = item.MissedFrames;
            }
            // Controller uptime and frame sequences are not portable across process restarts.
            InterruptTiming();
        }
    }
}
