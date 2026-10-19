namespace YATSS;

public sealed record HeatRaceRerun(int HeatNumber, int DiscardedAttempt, string Reason,
    DateTimeOffset RecordedAt, long DiscardedElapsedMilliseconds,
    IReadOnlyList<HeatRaceLaneResult> LaneResults, IReadOnlyList<HeatRaceLapRecord> Laps,
    IReadOnlyList<HeatRaceManualAdjustment> ManualAdjustments)
{
    public IReadOnlyDictionary<int, DistanceApproval> FinalDistanceApprovals { get; init; } = new Dictionary<int, DistanceApproval>();
}

public sealed partial class HeatRaceController
{
    private readonly List<HeatRaceRerun> _reruns = new();

    public bool CanRerunCurrentHeat
    {
        get { lock (_gate) return State is HeatRaceState.Paused or HeatRaceState.Complete; }
    }

    public bool RerunCurrentHeat(IReadOnlyList<LapRaceLaneSnapshot> snapshots, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A rerun reason is required.");
        lock (_gate)
        {
            if (!CanRerunCurrentHeat) return false;
            RecordHeatResults(snapshots);
            _reruns.Add(new(HeatNumber, _reruns.Count(item => item.HeatNumber == HeatNumber) + 1,
                reason.Trim(), DateTimeOffset.Now, _activeMillisecondsBeforeRun,
                _laneResults.Where(item => item.HeatNumber == HeatNumber).ToArray(),
                _laps.Where(item => item.HeatNumber == HeatNumber).ToArray(),
                _manualAdjustments.Where(item => item.HeatNumber == HeatNumber).ToArray())
            {
                FinalDistanceApprovals = _finalApprovals.Where(item => _laneRacers.Any(racer =>
                    !string.IsNullOrWhiteSpace(racer.Name) && GetRacerId(racer.Name) == item.Key))
                    .ToDictionary(item => item.Key, item => item.Value)
            });
            _laneResults.RemoveAll(item => item.HeatNumber == HeatNumber);
            _laps.RemoveAll(item => item.HeatNumber == HeatNumber);
            _manualAdjustments.RemoveAll(item => item.HeatNumber == HeatNumber);
            if (State == HeatRaceState.Complete) _raceTimestampBase -= Format == RaceFormat.LapFinish ? _activeMillisecondsBeforeRun : _heatLengthMilliseconds;
            LapFinish = null;
            if (Format == RaceFormat.CombinedDistance)
            {
                foreach (RacerEntry racer in _laneRacers.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
                    _finalApprovals.Remove(GetRacerId(racer.Name));
                _directorTieOrder.Clear();
                _finalTieOrderRecordedAt = null;
            }
            _activeMillisecondsBeforeRun = 0;
            _hasRunStartedAt = false;
            Array.Fill(_laneSeenThisHeat, false);
            _recoveryHistoryVersion++;
            State = HeatRaceState.Ready;
            return true;
        }
    }
}
