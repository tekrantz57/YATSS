namespace YATSS;

internal sealed record ControllerTestStatus(string? Session, bool Ready, bool NeedsResume, uint ConfirmedTimestamp,
    HeatRaceState HeatState, TimeSpan Remaining, QualifyingState QualifyingState, string Qualifier,
    int CompletedQualifiers, IReadOnlyList<int> Laps, IReadOnlyList<int> BestLaps, bool Countdown);

public sealed partial class Serial
{
    internal ControllerTestStatus CaptureTestStatus()
    {
        if (AppContext.GetData("YATSS.TestDataDirectory") is not string)
            throw new InvalidOperationException("Test status requires isolated regression data");
        lock (_controlGate)
            return new(_timingSession.Id, _controllerSession.IsReady && CaptureReady, _controllerSession.NeedsResume,
                _controllerSession.LastConfirmedTimestamp, _heatRace.State, _heatRace.GetRemaining(GetCurrentControllerTimestamp()),
                _qualifying.State, _qualifying.CurrentRacer, _qualifying.GetRankedResults().Count,
                Enumerable.Range(0, 8).Select(lane => _race.GetLane(lane).getCount()).ToArray(),
                Enumerable.Range(0, 8).Select(lane => _race.GetLane(lane).best_time).ToArray(), _startCountdownInProgress);
    }
}
