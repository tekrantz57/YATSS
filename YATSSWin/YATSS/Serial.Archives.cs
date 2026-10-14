namespace YATSS;

public sealed partial class Serial
{
    private bool _archiveBrowsing;
    public bool CanBrowseArchives
    {
        get
        {
            lock (_controlGate) return EventChangesAllowed && _recovery != null && !_trackPowerEnabled &&
                !_startCountdownInProgress && !_diagnosticsActive && !_firmwareUpdateActive && !_distanceApprovalPending &&
                !_qualifyingLaneSelectionPending && _heatRace.State != HeatRaceState.Running && _qualifying.State != QualifyingState.Running;
        }
    }

    public bool BeginArchiveBrowsing()
    {
        lock (_controlGate)
        {
            if (_archiveBrowsing || !CanBrowseArchives) return false;
            CancelBetweenHeatsTimer();
            _betweenHeatsPaused = _heatRace.State == HeatRaceState.Complete && _heatRace.HasMoreHeats;
            _archiveBrowsing = true;
            if (!SaveRecovery("Archive browser opened; automatic start paused")) { _archiveBrowsing = false; return false; }
            return true;
        }
    }

    public void EndArchiveBrowsing()
    {
        lock (_controlGate)
        {
            _archiveBrowsing = false;
            if (!EventChangesAllowed) return;
            if (_betweenHeatsPaused) PublishHeatRaceStatus("Intermission paused");
            _form.SetStatusMessage("Archive browser closed. Track power remains off; press Space when ready.");
        }
    }

    private T WithArchives<T>(Func<RaceRecoveryStore, T> action)
    {
        lock (_controlGate)
        {
            if (!_archiveBrowsing || !CanBrowseArchives) throw new InvalidOperationException("Archive access is available only while stopped with power off.");
            return action(_recovery!);
        }
    }

    public RaceArchivePage ListRaceArchives(string search, int page) => WithArchives(store => store.ListArchives(search, page));
    public RaceRecoveryFrame ReadRaceArchive(RaceArchiveEntry entry) => WithArchives(store => store.ReadArchive(entry));
    public IReadOnlyList<RaceArchiveEntry> FindOlderArchives(DateTimeOffset cutoff) => WithArchives(store => store.ArchivesOlderThan(cutoff));
    public int DeleteRaceArchives(IReadOnlyList<RaceArchiveEntry> entries) => WithArchives(store => store.DeleteArchives(entries));
}
