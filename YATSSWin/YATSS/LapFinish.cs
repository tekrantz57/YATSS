namespace YATSS;

public sealed record LapFinishOutcome(int WinnerLaneIndex, long ElapsedMilliseconds);
public sealed record LapFinishStanding(int Place, int LaneIndex, string RacerName,
    int CompletedLaps, long? LastCrossingElapsedMilliseconds, bool Winner);

public sealed partial class HeatRaceController
{
    public const int MaximumLapTarget = 1_000_000;
    public int LapTarget { get; private set; }
    public LapFinishOutcome? LapFinish { get; private set; }

    public TimeSpan GetElapsed(uint timestamp)
    {
        lock (_gate) return TimeSpan.FromMilliseconds(GetElapsedMillisecondsCore(timestamp));
    }

    // Called only after LapRace accepts a counted crossing, inside Serial's control gate.
    public bool TryCompleteLapFinish(int laneIndex, int acceptedLapCount, uint timestamp)
    {
        lock (_gate)
        {
            if (Format != RaceFormat.LapFinish || State != HeatRaceState.Running ||
                laneIndex is < 0 or >= LapProtocolParser.LaneCount ||
                string.IsNullOrWhiteSpace(_laneRacers[laneIndex].Name) || acceptedLapCount < LapTarget)
                return false;
            long elapsed = GetElapsedMillisecondsCore(timestamp);
            LapFinish = new(laneIndex, elapsed);
            _activeMillisecondsBeforeRun = elapsed;
            _raceTimestampBase = elapsed;
            _hasRunStartedAt = false;
            State = HeatRaceState.Complete;
            return true;
        }
    }

    internal static IReadOnlyList<LapFinishStanding> RankLapFinish(HeatRaceReport report)
    {
        var ordered = report.LaneResults.Select(result => new LapFinishStanding(0, result.LaneIndex,
            result.RacerName, result.TotalLaps,
            report.Laps.Where(lap => lap.LaneIndex == result.LaneIndex)
                .Select(lap => (long?)lap.RaceElapsedMilliseconds).LastOrDefault(),
            report.LapFinish?.WinnerLaneIndex == result.LaneIndex))
            .OrderByDescending(row => row.Winner).ThenByDescending(row => row.CompletedLaps)
            .ThenBy(row => row.LastCrossingElapsedMilliseconds ?? long.MaxValue)
            .ThenBy(row => row.LaneIndex).ToArray();
        for (int index = 0; index < ordered.Length; index++)
        {
            bool tied = index > 0 && !ordered[index].Winner && !ordered[index - 1].Winner &&
                ordered[index].CompletedLaps == ordered[index - 1].CompletedLaps &&
                ordered[index].LastCrossingElapsedMilliseconds == ordered[index - 1].LastCrossingElapsedMilliseconds;
            ordered[index] = ordered[index] with { Place = tied ? ordered[index - 1].Place : index + 1 };
        }
        return ordered;
    }
}
