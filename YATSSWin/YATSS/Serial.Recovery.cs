using System.Threading.Channels;

namespace YATSS;

public sealed partial class Serial
{
    private static string RecoveryPath => Path.Combine(Path.GetDirectoryName(AppDatabase.DatabasePath)!, "ActiveRace.db");
    private RaceRecoveryStore? _recovery;
    private RaceRecoveryFrame? _pendingRecovery;
    private Guid _eventId;
    private bool _recoveryArmed;
    private bool _recoveryFailed;
    private bool _savingRecovery;
    private bool _reportWritten;
    private bool _eventDemo;
    private string? _recoveryError;
    private System.Threading.Timer? _recoveryTimer;
    private Task? _processingTask;
    private readonly Channel<(string Line, int Generation)> _receivedLines =
        Channel.CreateBounded<(string, int)>(new BoundedChannelOptions(2048)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    public bool EventChangesAllowed => !_recoveryFailed && _pendingRecovery == null;

    private void OpenRecoveryJournal()
    {
        try
        {
            _recovery = new RaceRecoveryStore(RecoveryPath);
            _pendingRecovery = _recovery.Read();
            if (_pendingRecovery?.ReportWritten == true)
            {
                _recovery.Archive("Completed event with successful exports");
                _pendingRecovery = null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException or ArgumentException)
        {
            _recoveryFailed = true;
            _recoveryError = ex.Message;
            _log.Error(ex, "active-race journal unavailable");
        }
        _recoveryTimer = new System.Threading.Timer(_ =>
        {
            lock (_controlGate)
            {
                if (_heatRace.State == HeatRaceState.Running || _qualifying.State == QualifyingState.Running)
                    SaveRecovery("Active-time checkpoint");
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void OfferRaceRecovery()
    {
        if (_recoveryFailed)
        {
            MessageBox.Show(_form, $"The active-race journal could not be opened. Racing is disabled.\n\n{_recoveryError}\n\n{RecoveryPath}\n\nPreserve this file and restart after correcting the problem.",
                "Race Recovery Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        RaceRecoveryFrame? saved = _pendingRecovery;
        if (saved == null) return;
        using RaceRecoveryChoiceForm choice = new(saved);
        DialogResult result = choice.ShowDialog(_form);
        if (result == DialogResult.Cancel) { _form.Close(); return; }
        lock (_controlGate)
        {
            try
            {
                if (result == DialogResult.No)
                {
                    _recovery!.Archive("Director discarded unfinished event at startup");
                    _pendingRecovery = null;
                    _form.SetStatusMessage("Unfinished event archived; practice ready with power off");
                    return;
                }
                RestoreEvent(saved);
                _pendingRecovery = null;
                _recoveryArmed = true;
                if (!SaveRecovery("Director restored unfinished event with power off")) return;
                if (saved.Demo) StartDemoLapStream();
                _form.RestoreEventBoard(saved.Heat, _race.GetLaneSnapshots());
                if (_qualifying.State != QualifyingState.Inactive)
                {
                    QualifyingResult? unapproved = _qualifying.GetRankedResults()
                        .OrderBy(item => item.OriginalOrder).LastOrDefault(item => item.Distance == null);
                    if (CombinedDistanceActive && unapproved != null)
                        RequestQualifyingDistanceApproval(unapproved);
                    else if (_qualifying.State == QualifyingState.Complete) BeginQualifyingLaneSelection();
                    else { PrepareCurrentQualifier(); PublishQualifyingStatus("Ready"); }
                }
                else if (_heatRace.State == HeatRaceState.Complete)
                {
                    _betweenHeatsPaused = _heatRace.HasMoreHeats;
                    if (CombinedDistanceActive && _heatRace.IsGroupEnd && _heatRace.GetFinalDistanceCandidates().Count > 0)
                        ConfirmNextFinalDistance();
                    else if (!_heatRace.HasMoreHeats) ScheduleNextHeatIfNeeded();
                    else PublishHeatRaceStatus("Intermission paused");
                }
                else PublishHeatRaceStatus(GetCurrentHeatStatusName());
                _form.SetStatusMessage("Event restored with power off. Review totals and car positions; press Space when ready. Unrecorded outage crossings are uncertain.");
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or
                Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
            {
                FailRecovery(ex);
            }
        }
    }

    private void RestoreEvent(RaceRecoveryFrame saved)
    {
        _trackPowerEnabled = false;
        CancelStartCountdown(); CancelBetweenHeatsTimer(); ClearDistanceWorkflow();
        _heatRace.RestoreRecovery(saved.Heat);
        _qualifying.RestoreRecovery(saved.Qualifying);
        _race.RestoreRecovery(saved.Options, saved.Lanes);
        if (saved.Qualifying.State is QualifyingState.Running or QualifyingState.Paused) _race.Reset();
        _eventId = saved.EventId;
        _configuredFormat = saved.Heat.Format;
        _configuredRaceName = saved.Heat.RaceName;
        _configuredHeatLengthMinutes = saved.Heat.HeatLengthMinutes;
        _configuredBetweenHeatsSeconds = saved.Heat.BetweenHeatsSeconds;
        _configuredActiveLaneCount = saved.Heat.ActiveLaneCount;
        _configuredTrackLengthFeet = saved.Heat.TrackLengthFeet;
        _configuredLaneConfigurations = saved.Heat.LaneConfigurations;
        _configuredRacers = saved.ConfiguredRacers;
        _qualifyingResults = saved.Heat.Qualifiers;
        _reportWritten = saved.ReportWritten;
        _eventDemo = saved.Demo;
        _qualifyingLaneSelectionPending = false;
        _demoLapTiming.ConfigureRacers(_configuredRacers);
        _form.RestoreEventSettings(saved);
    }

    private bool SaveRecovery(string reason)
    {
        if (!_recoveryArmed || _savingRecovery) return !_recoveryFailed;
        if (!EventChangesAllowed || _recovery == null) return false;
        _savingRecovery = true;
        try
        {
            uint timestamp = DemoLapStreamActive ? GetCurrentControllerTimestamp() : _controllerSession.LastConfirmedTimestamp;
            RaceRecoveryFrame? prior = _recovery.Cursor;
            _recovery.Save(new(RaceRecoveryStore.SchemaVersion, _eventId, DateTimeOffset.Now, reason,
                _heatRace.CaptureRecovery(timestamp, prior?.Heat.HistoryVersion), _qualifying.CaptureRecovery(),
                _race.Options, _race.CaptureRecovery(prior?.Lanes), _eventDemo, _reportWritten,
                _configuredRacers.ToArray(), _form.SensorDebounceMilliseconds));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
            Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
        {
            FailRecovery(ex);
            return false;
        }
        finally { _savingRecovery = false; }
    }

    private bool ArchiveActiveEvent(string reason)
    {
        if (!EventChangesAllowed) return false;
        if (_recovery == null) return true; // Initial practice setup precedes journal opening.
        _trackPowerEnabled = false;
        if (IsPortOpen()) TryWriteLine("TRACK_POWER:MASK:00");
        try
        {
            _recovery.Archive(reason);
            _recoveryArmed = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { FailRecovery(ex); return false; }
    }

    private void FailRecovery(Exception ex)
    {
        bool first = !_recoveryFailed;
        _recoveryFailed = true;
        _trackPowerEnabled = false;
        CancelStartCountdown(); CancelBetweenHeatsTimer();
        if (IsPortOpen()) TryWriteLine("TRACK_POWER:MASK:00");
        _heatRace.Pause(_controllerSession.LastConfirmedTimestamp);
        _qualifying.Pause(_controllerSession.LastConfirmedTimestamp);
        _log.Error(ex, "race recovery persistence failed; racing stopped");
        _form.SetStatusMessage("RACE SAVE FAILED - POWER OFF. Restart after fixing storage; recover the last committed result.");
        if (first && _form.IsHandleCreated && !_form.IsDisposed)
            _form.BeginInvoke(() => MessageBox.Show(_form,
                $"Racing has stopped because the event could not be saved.\n\n{ex.Message}\n\nThe last committed event remains in:\n{RecoveryPath}\n\nFix the storage problem and restart YATSS. A crossing not displayed as accepted may need director review.",
                "Race Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error));
    }

    private async Task ProcessReceivedLinesAsync()
    {
        try
        {
            await foreach (var item in _receivedLines.Reader.ReadAllAsync(_stop.Token))
            {
                lock (_controlGate)
                {
                    if (item.Generation != Volatile.Read(ref _connectionGeneration)) continue;
                    HandleLine(item.Line, isDemoLine: false);
                    if (_controllerSession.HeartbeatExpired)
                    {
                        HandleControllerFault("Controller heartbeats stopped");
                        RequestReconnect();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            lock (_controlGate) FailRecovery(ex);
        }
    }
}

public sealed class RaceRecoveryChoiceForm : Form
{
    public RaceRecoveryChoiceForm(RaceRecoveryFrame saved)
    {
        Text = "Unfinished Event";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(660, 300);
        Font = new Font("Segoe UI", 10F);
        string stage = saved.Qualifying.State != QualifyingState.Inactive ?
            $"Qualifying {Math.Min(saved.Qualifying.CurrentIndex + 1, saved.Qualifying.Racers.Length)}/{saved.Qualifying.Racers.Length}" :
            $"Heat {saved.Heat.HeatNumber}/{saved.Heat.TotalHeats}";
        string state = saved.Qualifying.State != QualifyingState.Inactive ? saved.Qualifying.State.ToString() :
            $"{saved.Heat.State} - {YATSS.FormatClock(TimeSpan.FromMilliseconds(saved.Heat.HeatLengthMinutes * 60000L - saved.Heat.ElapsedMilliseconds))} remaining";
        Label description = new() { Dock = DockStyle.Fill, Padding = new Padding(20), AutoSize = false,
            Text = $"{saved.Heat.RaceName}\n\n{stage} - {state}\nLast saved: {saved.SavedAt.LocalDateTime:g}\n\nResume keeps power off. Check totals and physical car positions before pressing Space. Crossings after the last save cannot be reconstructed. An interrupted qualifier must be rerun." };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12), FlowDirection = FlowDirection.RightToLeft };
        Button cancel = new() { Text = "Close YATSS", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button discard = new() { Text = "Archive and Discard", DialogResult = DialogResult.No, AutoSize = true };
        Button resume = new() { Text = "Resume Event", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { cancel, discard, resume });
        Controls.Add(description); Controls.Add(buttons);
        AcceptButton = resume; CancelButton = cancel;
    }
}
