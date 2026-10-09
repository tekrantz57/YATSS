namespace YATSS;

public partial class YATSS
{
    public void RestoreEventSettings(RaceRecoveryFrame saved)
    {
        ActiveLaneCount = saved.Heat.ActiveLaneCount;
        LaneConfigurations = saved.Heat.LaneConfigurations;
        TrackLengthFeet = saved.Options.TrackLengthFeet;
        MinLapMilliseconds = saved.Options.MinLapMilliseconds;
        RawSensorLockoutMilliseconds = saved.Options.RawSensorLockoutMilliseconds;
        SensorDebounceMilliseconds = saved.SensorDebounceMilliseconds;
        ApplyLaneColors(); ApplyActiveLaneLayout(); SetHeatRaceMode();
        SetRaceTitle(saved.Heat.RaceName);
        SetQualifyingAvailable(saved.Heat.State == HeatRaceState.Ready && saved.Qualifying.State == QualifyingState.Inactive);
    }

    public void RestoreEventBoard(HeatRecovery heat, IReadOnlyList<LapRaceLaneSnapshot> lanes)
    {
        ResetBoardDisplay(clearRacers: true);
        SetLaneRacerNames(heat.Lanes.Select(lane => lane.Name).ToArray());
        foreach (LapRaceLaneSnapshot lane in lanes)
        {
            Lane stats = new(lane.LaneIndex);
            stats.Restore(lane);
            UpdateLaneDisplay(lane.LaneIndex, lane.TotalLapCount,
                lane.Laps.LastOrDefault()?.LapMilliseconds is int last ? (last / 1000d).ToString("0.000") : string.Empty,
                lane.BestLapMilliseconds is int best ? (best / 1000d).ToString("0.000") : string.Empty,
                stats.getMedian() > 0 ? (stats.getMedian() / 1000d).ToString("0.000") : string.Empty);
        }
    }
}
