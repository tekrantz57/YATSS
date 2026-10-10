namespace YATSS;

public sealed partial class Serial
{
    private Guid _rerunRequest;
    private Guid _rerunEvent;
    private int _rerunHeat;
    private bool CanRerunCore() => EventChangesAllowed && !QualifyingActive && !_trackPowerEnabled &&
        !_startCountdownInProgress && !_diagnosticsActive && !_firmwareUpdateActive &&
        !_distanceApprovalPending && !_reportWritten && _exportPlan == null && _heatRace.CanRerunCurrentHeat;

    public bool CanRerunCurrentHeat
    {
        get { lock (_controlGate) return _rerunRequest == Guid.Empty && CanRerunCore(); }
    }

    public Guid BeginHeatRerunConfirmation()
    {
        lock (_controlGate)
        {
            if (!CanRerunCurrentHeat) return Guid.Empty;
            _rerunRequest = Guid.NewGuid();
            _rerunEvent = _eventId; _rerunHeat = _heatRace.HeatNumber;
            CancelStartCountdown(); CancelBetweenHeatsTimer();
            _betweenHeatsPaused = _heatRace.State == HeatRaceState.Complete;
            if (!SaveRecovery("Director considering current heat rerun"))
            { _rerunRequest = Guid.Empty; return Guid.Empty; }
            return _rerunRequest;
        }
    }

    public bool FinishHeatRerunConfirmation(Guid request, string? reason)
    {
        lock (_controlGate)
        {
            if (request == Guid.Empty || request != _rerunRequest) return false;
            _rerunRequest = Guid.Empty;
            if (_eventId != _rerunEvent || _heatRace.HeatNumber != _rerunHeat || !CanRerunCore()) return false;
            if (string.IsNullOrWhiteSpace(reason))
            {
                PublishHeatRaceStatus(_heatRace.State == HeatRaceState.Complete ? "Intermission paused" : "Paused");
                _form.SetStatusMessage("Rerun cancelled; heat unchanged. Automatic start remains paused. Press Space when ready.");
                return false;
            }
            _trackPowerEnabled = false;
            if (IsPortOpen()) TryWriteLine("TRACK_POWER:MASK:00");
            if (!_heatRace.RerunCurrentHeat(_race.GetLaneSnapshots(), reason)) return false;
            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(0);
            _race.ResetTimingForHeat(snapshot.LaneLapCounts);
            _betweenHeatsPaused = false;
            _raceConfigurationVersion++;
            if (!SaveRecovery("Director voided heat attempt: " + reason.Trim())) return false;
            _form.SetLaneRacerNames(snapshot.LaneRacers);
            _form.ResetHeatTimingDisplay(snapshot.LaneLapCounts);
            PublishHeatRaceStatus("Ready");
            _log.Info($"Heat {snapshot.HeatNumber} attempt voided; rerun ready: {reason.Trim()}");
            _form.SetStatusMessage($"Heat {snapshot.HeatNumber} rerun ready, power off. Restore cars to their heat-start positions; press Space when ready.");
            return true;
        }
    }
}
