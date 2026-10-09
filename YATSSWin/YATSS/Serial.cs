using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Media;
using System.Net.Sockets;

namespace YATSS
{
    public sealed partial class Serial : IDisposable
    {
        private readonly YATSS _form;
        private readonly LapRace _race = new();
        private readonly HeatRaceController _heatRace = new();
        private readonly QualifyingController _qualifying = new();
        private readonly SerialLog _log = new();
        private readonly DemoLapTiming _demoLapTiming = new();
        private readonly LapBestSoundPlayer? _lapBestSoundPlayer = LapBestSoundPlayer.TryCreate();
        private readonly RaceReportService _raceReports;
        private readonly CancellationTokenSource _stop = new();
        private readonly object _portGate = new();
        private readonly object _reconnectGate = new();
        private readonly object _demoGate = new();
        private readonly object _controlGate = new();
        private readonly ControllerSession _controllerSession = new();
        private bool _controllerFaultActive;
        private bool _controllerClockResetPending;
        private string _controllerRecoveryReason = "Waiting for controller";
        private TaskCompletionSource _reconnectNow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _readerTask;
        private Task? _demoTask;
        private IControllerConnection? _port;
        private int _connectionGeneration;
        private CancellationTokenSource? _demoStop;
        private Stopwatch? _demoClock;
        private uint _demoStartTimestamp;
        private bool _demoClockActive;
        private System.Threading.Timer? _betweenHeatsTimer;
        private System.Threading.Timer[] _betweenHeatsAnnouncementTimers = Array.Empty<System.Threading.Timer>();
        private int _betweenHeatsVersion;
        private bool _betweenHeatsPaused;
        private DateTime? _nextHeatStartUtc;
        private DateTime? _lastControllerResponseUtc;
        private uint _latestControllerTimestamp;
        private bool _hasControllerTimestamp;
        private bool _trackPowerEnabled;
        private bool _diagnosticsActive;
        private volatile bool _firmwareUpdateActive;
        private ControllerIdentity? _controllerIdentity;
        private bool _startCountdownInProgress;
        private int _startCountdownVersion;
        private bool _qualifyingLaneSelectionPending;
        private string _configuredRaceName = string.Empty;
        private int _configuredHeatLengthMinutes;
        private int _configuredBetweenHeatsSeconds;
        private int _configuredActiveLaneCount = LapProtocolParser.LaneCount;
        private double _configuredTrackLengthFeet = LapRaceOptions.Default.TrackLengthFeet;
        private IReadOnlyList<string> _configuredRacers = Array.Empty<string>();
        private IReadOnlyList<LaneConfiguration> _configuredLaneConfigurations =
            LaneConfiguration.CreateDefaults();
        private IReadOnlyList<QualifyingResult> _qualifyingResults = Array.Empty<QualifyingResult>();
        private RaceFormat _configuredFormat;
        private bool _distanceApprovalPending;
        private volatile int _raceConfigurationVersion;
        public bool CombinedDistanceActive => _configuredFormat == RaceFormat.CombinedDistance && _heatRace.State != HeatRaceState.Practice;
        private static readonly TimeSpan ControllerPingInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ControllerPingTimeout = TimeSpan.FromSeconds(3);

        public Serial(YATSS form)
        {
            _form = form;
            _log.Warning += message => _form.SetStatusMessage(message);
            _raceReports = new RaceReportService(form, _log);
            Init();
            ApplySettings();
            OpenRecoveryJournal();
            _processingTask = Task.Run(ProcessReceivedLinesAsync);
            if (!_recoveryFailed) _readerTask = Task.Run(ReadLoopAsync);
        }

        public bool QualifyingActive => _qualifying.State != QualifyingState.Inactive;

        public SerialLog Log => _log;

        public event Action<ControllerDiagnostic>? DiagnosticReceived;

        public bool DiagnosticsActive => _diagnosticsActive;

        public ControllerIdentity? CurrentControllerIdentity => Volatile.Read(ref _controllerIdentity);

        public bool DemoLapStreamActive
        {
            get
            {
                lock (_demoGate)
                {
                    return _demoTask is { IsCompleted: false };
                }
            }
        }

        public void ApplySettings()
        {
            LapRaceOptions options = _race.Options;
            _race.SetOptions(options with
            {
                MinLapMilliseconds = _form.MinLapMilliseconds,
                TrackLengthFeet = _form.TrackLengthFeet,
                RawSensorLockoutMilliseconds = _form.RawSensorLockoutMilliseconds
            });
            _log.Info(
                $"minimum lap time set to {_form.MinLapMilliseconds} ms; " +
                $"track length set to {_form.TrackLengthFeet:0.##} ft; " +
                $"controller debounce set to {_form.SensorDebounceMilliseconds} ms; " +
                $"Windows raw edge lockout set to {_form.RawSensorLockoutMilliseconds} ms; " +
                $"sound on too-fast laps is {_form.SoundOnTooFastLap}; " +
                $"lap-best sounds are {_form.LapBestSoundsEnabled}");
            if (_heatRace.State == HeatRaceState.Practice && IsPortOpen())
            {
                WriteLine(GetSensorDebounceCommand());
                WriteLine(GetTrackPowerCommand());
            }

            _form.SetStatusMessage($"Minimum lap time {_form.MinLapMilliseconds} ms");
            lock (_controlGate) SaveRecovery("Timing settings changed");
        }

        public void RefreshActiveStatus()
        {
            lock (_controlGate)
            {
                RefreshActiveStatusCore();
            }
        }

        private void RefreshActiveStatusCore()
        {
            if (!DemoLapStreamActive && _controllerSession.HeartbeatExpired)
            {
                HandleControllerFault("Controller heartbeats stopped");
                RequestReconnect();
            }
            if (!DemoLapStreamActive && _controllerSession.NeedsResume)
            {
                PublishControllerRecoveryStatus();
                return;
            }
            if (_distanceApprovalPending) return;
            uint controllerTimestamp = GetCurrentControllerTimestamp();
            uint confirmedTimestamp = DemoLapStreamActive
                ? controllerTimestamp : _controllerSession.LastConfirmedTimestamp;
            if (CheckQualifyingExpired(confirmedTimestamp))
            {
                return;
            }

            if (_qualifying.State != QualifyingState.Inactive)
            {
                PublishQualifyingStatus(GetQualifyingStateDisplayName());
                return;
            }

            if (_heatRace.State == HeatRaceState.Practice)
            {
                return;
            }

            if (!CheckHeatExpired(confirmedTimestamp))
            {
                PublishHeatRaceStatus(GetCurrentHeatStatusName());
            }
        }

        public void SetPort(string portName)
        {
            lock (_controlGate)
            {
                SetPortCore(portName);
            }
        }

        private void SetPortCore(string portName)
        {
            portName = portName.Trim();
            if (string.Equals(_form.port, portName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _form.port = portName;
            HandleControllerFault("Controller connection changed");
            Volatile.Write(ref _controllerIdentity, null);
            SavePort(portName);
            _log.Info(string.IsNullOrWhiteSpace(portName) ? "serial port cleared" : $"serial port set to {portName}");
            _form.SetStatusMessage(string.IsNullOrWhiteSpace(portName) ? "No serial port configured" : $"Serial port set to {portName}");
            RequestReconnect();
        }

        public void Init()
        {
            lock (_controlGate)
            {
                InitCore();
            }
        }

        private void InitCore()
        {
            if (!ArchiveActiveEvent("Event reset")) return;
            ClearDistanceWorkflow();
            StopControllerDiagnostics();
            StopDemoLapStream();
            CancelStartCountdown();
            CancelBetweenHeatsTimer();
            _qualifying.Reset();
            _qualifyingLaneSelectionPending = false;
            _heatRace.SetPracticeMode();
            _race.Reset();
            _form.ResetBoardDisplay(clearRacers: true);
            _form.ClearHeatRaceStatus();
            _form.SetQualifyingAvailable(false);
            _log.Info("race state reset");
            _form.SetStatusMessage("Practice reset");
        }

        public void ResetRace(bool resetArduino)
        {
            if (!EventChangesAllowed) return;
            Init();
            if (resetArduino)
            {
                HandleControllerFault("Controller reset requested");
                WriteLine("RESET");
                RequestReconnect("Waiting for controller after reset");
            }
        }

        public void ResetLane(int laneIndex)
        {
            lock (_controlGate)
            {
                ResetLaneCore(laneIndex);
            }
        }

        private void ResetLaneCore(int laneIndex)
        {
            if (!EventChangesAllowed) return;
            if (CombinedDistanceActive) { _form.SetStatusMessage("Lane reset is unavailable during Combined Distance; use stopped-time lap corrections."); return; }
            if (laneIndex < 0 || laneIndex >= LapProtocolParser.LaneCount)
            {
                return;
            }

            _race.ResetLane(laneIndex);
            if (!SaveRecovery("Lane reset")) return;
            _form.ResetLaneDisplay(laneIndex, clearRacer: false);
            _log.Info($"lane {laneIndex}: lane state reset");
            _form.SetStatusMessage($"Lane {laneIndex + 1} reset");
        }

        public void SetTrackPowerEnabled(bool enabled)
        {
            SetTrackPowerEnabled(enabled, enabled ? "Let's go" : "Track call",
                enabled ? "Track power restore requested" : "Track power cut requested");
        }

        public void ConfigureHeatRace(
            string raceName,
            int heatLengthMinutes,
            int betweenHeatsSeconds,
            IReadOnlyList<string> racers,
            int activeLaneCount,
            IReadOnlyList<LaneConfiguration> laneConfigurations,
            double trackLengthFeet,
            RaceFormat format = RaceFormat.HeatRace)
        {
            lock (_controlGate)
            {
                ConfigureHeatRaceCore(raceName, heatLengthMinutes, betweenHeatsSeconds,
                    racers, activeLaneCount, laneConfigurations, trackLengthFeet, format);
            }
        }

        private void ConfigureHeatRaceCore(string raceName, int heatLengthMinutes, int betweenHeatsSeconds,
            IReadOnlyList<string> racers, int activeLaneCount,
            IReadOnlyList<LaneConfiguration> laneConfigurations, double trackLengthFeet, RaceFormat format)
        {
            if (!ArchiveActiveEvent("Replaced by a new event")) return;
            _eventId = Guid.NewGuid();
            _reportWritten = false;
            _eventDemo = DemoLapStreamActive;
            _recoveryArmed = true;
            ClearDistanceWorkflow();
            _configuredFormat = format;
            StopControllerDiagnostics();
            CancelStartCountdown();
            CancelBetweenHeatsTimer();
            _qualifying.Reset();
            _qualifyingLaneSelectionPending = false;
            _race.Reset();
            _form.ResetBoardDisplay(clearRacers: false);
            _configuredRaceName = raceName;
            _configuredHeatLengthMinutes = heatLengthMinutes;
            _configuredBetweenHeatsSeconds = betweenHeatsSeconds;
            _configuredRacers = racers.ToArray();
            _configuredActiveLaneCount = activeLaneCount;
            _configuredLaneConfigurations = laneConfigurations.ToArray();
            _configuredTrackLengthFeet = trackLengthFeet;
            _qualifyingResults = Array.Empty<QualifyingResult>();
            _demoLapTiming.ConfigureRacers(_configuredRacers);
            _heatRace.Configure(
                heatLengthMinutes,
                betweenHeatsSeconds,
                racers,
                activeLaneCount,
                laneConfigurations,
                raceName,
                trackLengthFeet,
                _qualifyingResults,
                format);
            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetCurrentControllerTimestamp());
            _form.ResetHeatTimingDisplay(snapshot.LaneLapCounts);
            PublishHeatRaceStatus("Ready");
            SetTrackPowerEnabled(false, null, format == RaceFormat.CombinedDistance ?
                "Combined Distance requires qualifying. Select Mode > Qualifying." :
                $"Heat 1 ready: {heatLengthMinutes} minute heat. Press Space to start.");
            _log.Info($"heat race configured for {heatLengthMinutes} minute(s), {betweenHeatsSeconds} second(s) between heats");
        }

        public void SetPracticeMode()
        {
            lock (_controlGate)
            {
                SetPracticeModeCore();
            }
        }

        private void SetPracticeModeCore()
        {
            if (!ArchiveActiveEvent("Returned to practice")) return;
            ClearDistanceWorkflow();
            StopControllerDiagnostics();
            CancelStartCountdown();
            CancelBetweenHeatsTimer();
            _qualifying.Reset();
            _qualifyingLaneSelectionPending = false;
            _heatRace.SetPracticeMode();
            _race.Reset();
            _form.ResetBoardDisplay(clearRacers: true);
            _form.ClearHeatRaceStatus();
            _form.SetQualifyingAvailable(false);
            _form.SetStatusMessage("Practice mode");
        }

        public bool ToggleDemoLapStream()
        {
            StopControllerDiagnostics();
            lock (_demoGate)
            {
                if (_demoTask is { IsCompleted: false })
                {
                    _eventDemo = false;
                    _demoStop?.Cancel();
                    _form.SetStatusMessage("Demo lap stream stopping");
                    return false;
                }

                _demoStop?.Dispose();
                _eventDemo = true;
                _demoStop = new CancellationTokenSource();
                InitializeDemoControllerClockCore();
                _demoTask = Task.Run(() => RunDemoLapStreamAsync(_demoStop.Token));
                _form.SetStatusMessage("Demo lap stream started");
                _log.Info("DEMO: lap stream started");
                return true;
            }
        }

        public void StartDemoLapStream()
        {
            StopControllerDiagnostics();
            lock (_demoGate)
            {
                if (_demoTask is { IsCompleted: false })
                {
                    return;
                }

                _demoStop?.Dispose();
                _eventDemo = true;
                _demoStop = new CancellationTokenSource();
                InitializeDemoControllerClockCore();
                _demoTask = Task.Run(() => RunDemoLapStreamAsync(_demoStop.Token));
                _form.SetStatusMessage("Demo lap stream started");
                _form.SetDemoLapStreamChecked(true);
                _log.Info("DEMO: lap stream started");
            }
        }

        public void HandleSpaceBar()
        {
            lock (_controlGate)
            {
                HandleSpaceBarCore();
            }
        }

        private void HandleSpaceBarCore()
        {
            if (!EventChangesAllowed) { _form.SetStatusMessage("Race recovery needs attention; track power remains off"); return; }
            if (_distanceApprovalPending) { _form.SetStatusMessage("Director distance confirmation required"); return; }
            if (CombinedDistanceActive && !QualifyingActive && _qualifyingResults.Count == 0)
            { _form.SetStatusMessage("Select Mode > Qualifying before starting Combined Distance."); return; }
            if (_diagnosticsActive)
            {
                _form.SetStatusMessage("Close Controller Diagnostics before using race controls");
                return;
            }

            if (!DemoLapStreamActive && !_controllerSession.AuthorizeResume())
            {
                PublishControllerRecoveryStatus();
                return;
            }
            _controllerFaultActive = false;

            uint controllerTimestamp = GetCurrentControllerTimestamp();
            switch (_qualifying.State)
            {
                case QualifyingState.Ready:
                    QueueQualifyingCountdown(resumePausedQualifier: false);
                    return;
                case QualifyingState.Running:
                    if (_qualifying.Pause(controllerTimestamp))
                    {
                        PublishQualifyingStatus("Paused");
                        SetTrackPowerEnabled(false, "Track call", "Qualifying paused for track call. Press Space to resume.");
                        _log.Info($"{_qualifying.CurrentRacer} qualifying paused for track call");
                    }
                    return;
                case QualifyingState.Paused:
                    QueueQualifyingCountdown(resumePausedQualifier: true);
                    return;
                case QualifyingState.Complete:
                    _form.SetStatusMessage("Complete the qualifying lane selections");
                    return;
            }

            switch (_heatRace.State)
            {
                case HeatRaceState.Ready:
                    QueueStartCountdown(resumePausedHeat: false, manualStart: true);
                    break;
                case HeatRaceState.Running:
                    if (_heatRace.Pause(controllerTimestamp))
                    {
                        PublishHeatRaceStatus("Paused");
                        SetTrackPowerEnabled(false, "Track call", $"Heat paused for track call. {StoppedAdjustmentHint}");
                        _log.Info("heat paused for track call");
                    }
                    break;
                case HeatRaceState.Paused:
                    QueueStartCountdown(resumePausedHeat: true, manualStart: true);
                    break;
                case HeatRaceState.Complete:
                    if (!PauseBetweenHeats())
                    {
                        StartNextHeatFromComplete(manualStart: true);
                    }
                    break;
                default:
                    SetTrackPowerEnabled(!_trackPowerEnabled);
                    break;
            }
        }

        public void ConfigureQualifying(int laneIndex, int durationSeconds)
        {
            lock (_controlGate)
            {
                ConfigureQualifyingCore(laneIndex, durationSeconds);
            }
        }

        private void ConfigureQualifyingCore(int laneIndex, int durationSeconds)
        {
            if (!EventChangesAllowed) return;
            StopControllerDiagnostics();
            if (_heatRace.State != HeatRaceState.Ready || _configuredRacers.Count == 0)
            {
                _form.SetStatusMessage("Configure a heat race before qualifying");
                return;
            }

            CancelStartCountdown();
            CancelBetweenHeatsTimer();
            _qualifying.Configure(_configuredRacers, laneIndex, durationSeconds);
            _qualifyingLaneSelectionPending = false;
            _race.Reset();
            PrepareCurrentQualifier();
            SetTrackPowerEnabled(false, null, "Qualifying ready. Press Space to start the first qualifier.");
            _form.SetQualifyingAvailable(false);
            _log.Info(
                $"qualifying configured on lane {laneIndex + 1} for {durationSeconds} second(s) per racer");
        }

        public void CancelQualifying()
        {
            lock (_controlGate)
            {
                CancelQualifyingCore();
            }
        }

        private void CancelQualifyingCore()
        {
            if (!EventChangesAllowed) return;
            if (_qualifying.State == QualifyingState.Inactive)
            {
                return;
            }

            CancelStartCountdown();
            _qualifying.Reset();
            _qualifyingLaneSelectionPending = false;
            _qualifyingResults = Array.Empty<QualifyingResult>();
            _race.Reset();
            _heatRace.Configure(
                _configuredHeatLengthMinutes,
                _configuredBetweenHeatsSeconds,
                _configuredRacers,
                _configuredActiveLaneCount,
                _configuredLaneConfigurations,
                _configuredRaceName,
                _configuredTrackLengthFeet,
                _qualifyingResults,
                _configuredFormat);
            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetCurrentControllerTimestamp());
            _form.ResetBoardDisplay(clearRacers: false);
            _form.SetLaneRacerNames(snapshot.LaneRacers);
            _form.ResetHeatTimingDisplay(snapshot.LaneLapCounts);
            PublishHeatRaceStatus("Ready");
            SetTrackPowerEnabled(false, null, "Qualifying discarded. Heat 1 ready.");
            _form.SetQualifyingAvailable(true);
            _log.Info("qualifying discarded");
        }

        public bool AdjustStoppedHeatLap(int laneIndex, int delta)
        {
            lock (_controlGate)
            {
                return AdjustStoppedHeatLapCore(laneIndex, delta);
            }
        }

        private bool AdjustStoppedHeatLapCore(int laneIndex, int delta)
        {
            if (!EventChangesAllowed) return false;
            if (laneIndex < 0 || laneIndex >= _form.ActiveLaneCount)
            {
                _form.SetStatusMessage($"Lane {laneIndex + 1} is not configured");
                return false;
            }

            if (!_heatRace.CanAdjustLapCounts || (CombinedDistanceActive && _heatRace.State == HeatRaceState.Complete && _heatRace.IsGroupEnd))
            {
                _form.SetStatusMessage("Lap adjustment is only available during stopped heat time");
                return false;
            }

            int previousCount = _race.GetLane(laneIndex).getCount();
            int count = _race.AdjustLapCount(laneIndex, delta);
            _reportWritten = false;
            _heatRace.RecordManualLapAdjustment(laneIndex, count - previousCount, count);
            RecordCurrentHeatResults();
            if (!SaveRecovery("Director lap correction")) return false;
            Lane lane = _race.GetLane(laneIndex);
            string bestSeconds = lane.best_time == int.MaxValue ? string.Empty : FormatSeconds(lane.best_time);
            string medianSeconds = FormatOptionalSeconds(lane.getMedian());
            _form.UpdateLaneDisplay(laneIndex, count, string.Empty, bestSeconds, medianSeconds);

            string direction = delta > 0 ? "added to" : "subtracted from";
            _log.Info($"lane {laneIndex}: manual lap {direction} stopped heat, count {count}");
            _form.SetStatusMessage($"{StoppedAdjustmentHint} Lane {laneIndex + 1}: manual lap {direction} total, count {count}");
            return true;
        }

        private void SetTrackPowerEnabled(bool enabled, string? speech, string statusMessage)
        {
            lock (_controlGate)
            {
                SetTrackPowerEnabledCore(enabled, speech, statusMessage);
            }
        }

        private void SetTrackPowerEnabledCore(bool enabled, string? speech, string statusMessage)
        {
            if (enabled && !CanRunController())
            {
                PublishControllerRecoveryStatus();
                return;
            }
            bool restoreAfterCountdown = enabled &&
                string.Equals(speech, "Let's go", StringComparison.OrdinalIgnoreCase);

            if (restoreAfterCountdown)
            {
                if (_startCountdownInProgress)
                {
                    return;
                }
                _startCountdownInProgress = true;
                int countdownVersion = ++_startCountdownVersion;
                SpeechAnnouncer.SpeakCountdownAsync(
                    _form.SpeechVoiceName,
                    step => ShowCountdownStep(step, countdownVersion),
                    () =>
                    {
                        lock (_controlGate)
                        {
                            if (countdownVersion != _startCountdownVersion || !CanRunController())
                            {
                                return;
                            }
                            _trackPowerEnabled = true;
                            TryWriteLine(GetTrackPowerCommand());
                            if (_controllerSession.PowerAllowed || DemoLapStreamActive)
                            {
                                _form.ClearHeatRaceStatus();
                            }
                            FinishStartCountdown(countdownVersion);
                        }
                    },
                    () => IsCountdownCurrent(countdownVersion));
            }
            else
            {
                _trackPowerEnabled = enabled;
                WriteLine(GetTrackPowerCommand());
                if (!string.IsNullOrWhiteSpace(speech))
                {
                    SpeechAnnouncer.SpeakAsync(speech, _form.SpeechVoiceName);
                }
            }

            _log.Info(enabled ? "track power restore requested" : "track power cut requested");
            _form.SetStatusMessage(statusMessage);
        }

        public bool CanStartControllerDiagnostics(out string reason)
        {
            if (_heatRace.State != HeatRaceState.Practice || _qualifying.State != QualifyingState.Inactive)
            {
                reason = "Controller diagnostics are available only in Practice mode";
                return false;
            }

            if (_startCountdownInProgress)
            {
                reason = "Wait for the active countdown to finish";
                return false;
            }

            if (DemoLapStreamActive)
            {
                reason = "Stop the demo lap stream before opening controller diagnostics";
                return false;
            }

            if (!IsPortOpen() || !_controllerSession.PowerAllowed)
            {
                reason = "Connect the controller before opening diagnostics";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool CanRestoreDatabase(out string reason)
        {
            if (_heatRace.State != HeatRaceState.Practice || _qualifying.State != QualifyingState.Inactive)
            {
                reason = "Return to Practice mode before restoring the database";
                return false;
            }

            if (_startCountdownInProgress)
            {
                reason = "Wait for the active countdown to finish before restoring the database";
                return false;
            }

            if (DemoLapStreamActive)
            {
                reason = "Stop the demo lap stream before restoring the database";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool CanUpdateControllerFirmware(out string reason)
        {
            if (_heatRace.State != HeatRaceState.Practice || _qualifying.State != QualifyingState.Inactive)
            {
                reason = "Controller firmware can be updated only in Practice mode";
                return false;
            }

            if (_startCountdownInProgress)
            {
                reason = "Wait for the active countdown to finish before updating controller firmware";
                return false;
            }

            if (DemoLapStreamActive)
            {
                reason = "Stop Simulated Lap Input before updating controller firmware";
                return false;
            }

            if (_diagnosticsActive)
            {
                reason = "Close Controller Diagnostics before updating controller firmware";
                return false;
            }

            if (_firmwareUpdateActive)
            {
                reason = "A controller firmware update is already running";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_form.port))
            {
                reason = "Configure the controller COM port before updating firmware";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public async Task SuspendForFirmwareUpdateAsync()
        {
            if (!CanUpdateControllerFirmware(out string reason))
            {
                throw new InvalidOperationException(reason);
            }

            _firmwareUpdateActive = true;
            _trackPowerEnabled = false;
            if (IsPortOpen())
            {
                WriteLine("TRACK_POWER:OFF");
                await Task.Delay(250);
            }

            RequestReconnect();
            await WaitForPortToCloseAsync(TimeSpan.FromSeconds(3));
            Volatile.Write(ref _controllerIdentity, null);
            _log.Info("serial connection suspended for controller firmware update");
            _form.SetStatusMessage("Controller firmware update in progress");
        }

        public void ResumeAfterFirmwareUpdate()
        {
            _firmwareUpdateActive = false;
            _log.Info("serial connection resumed after controller firmware update");
            RequestReconnect("Waiting for controller after firmware update");
        }

        public async Task<bool> WaitForControllerIdentityAsync(
            string boardProfile,
            string firmwareVersion,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline && !_stop.IsCancellationRequested)
            {
                ControllerIdentity? identity = Volatile.Read(ref _controllerIdentity);
                if (identity != null &&
                    string.Equals(identity.BoardProfile, boardProfile, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(identity.FirmwareVersion, firmwareVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                await Task.Delay(200);
            }

            return false;
        }

        public void PrepareForDatabaseRestore()
        {
            StopControllerDiagnostics();
            SetTrackPowerEnabled(false, null, "Track power cut for database restore");
            _log.Info("database restore requested; track power cut");
        }

        public bool StartControllerDiagnostics(out string reason)
        {
            if (_diagnosticsActive)
            {
                reason = string.Empty;
                return true;
            }

            if (!CanStartControllerDiagnostics(out reason))
            {
                return false;
            }

            _diagnosticsActive = true;
            WriteLine("DIAG:START");
            _log.Info("controller diagnostics started");
            _form.SetStatusMessage("Controller diagnostics active");
            return true;
        }

        public void StopControllerDiagnostics()
        {
            if (!_diagnosticsActive)
            {
                return;
            }

            _diagnosticsActive = false;
            if (IsPortOpen())
            {
                WriteLine("DIAG:STOP");
            }
            _log.Info("controller diagnostics stopped");
        }

        public void RequestDiagnosticStatus()
        {
            if (_diagnosticsActive)
            {
                WriteLine("DIAG:STATUS");
            }
        }

        public void ClearDiagnosticCounts()
        {
            if (_diagnosticsActive)
            {
                WriteLine("DIAG:CLEAR");
            }
        }

        public void PulseDiagnosticRelay(int laneIndex, int durationMilliseconds = 1000)
        {
            if (!_diagnosticsActive || laneIndex < 0 || laneIndex >= LapProtocolParser.LaneCount)
            {
                return;
            }

            int duration = Math.Clamp(durationMilliseconds, 1, 2000);
            WriteLine($"DIAG:RELAY:PULSE:{laneIndex}:{duration}");
        }

        public void CutAllPowerDuringDiagnostics()
        {
            if (_diagnosticsActive)
            {
                SetTrackPowerEnabled(false, null, "Track power cut from Controller Diagnostics");
            }
        }

        private void QueueStartCountdown(bool resumePausedHeat, bool manualStart)
        {
            if (_startCountdownInProgress || !CanRunController())
            {
                return;
            }

            _startCountdownInProgress = true;
            int countdownVersion = ++_startCountdownVersion;
            _form.SetQualifyingAvailable(false);
            PublishHeatRaceStatus(resumePausedHeat ? "Resuming" : "Starting");
            _form.SetStatusMessage(resumePausedHeat ? "Heat restart countdown" : $"Heat {_heatRace.HeatNumber} countdown");
            _log.Info(resumePausedHeat ? "heat restart countdown queued" : $"heat {_heatRace.HeatNumber} start countdown queued");
            SpeechAnnouncer.SpeakCountdownAsync(
                _form.SpeechVoiceName,
                step => ShowCountdownStep(step, countdownVersion),
                () => CompleteStartCountdown(resumePausedHeat, manualStart, countdownVersion),
                () => IsCountdownCurrent(countdownVersion));
        }

        private void QueueQualifyingCountdown(bool resumePausedQualifier)
        {
            QualifyingState expectedState = resumePausedQualifier
                ? QualifyingState.Paused
                : QualifyingState.Ready;
            if (_startCountdownInProgress || _qualifying.State != expectedState || !CanRunController())
            {
                return;
            }

            _startCountdownInProgress = true;
            int countdownVersion = ++_startCountdownVersion;
            _form.SetQualifyingAvailable(false);
            PublishQualifyingStatus(resumePausedQualifier ? "Resuming" : "Starting");
            _form.SetStatusMessage(
                resumePausedQualifier
                    ? $"{_qualifying.CurrentRacer} qualifying restart countdown"
                    : $"Qualifier {_qualifying.CurrentNumber}/{_qualifying.RacerCount} countdown");
            _log.Info(resumePausedQualifier
                ? $"qualifier {_qualifying.CurrentNumber} restart countdown queued"
                : $"qualifier {_qualifying.CurrentNumber} start countdown queued");
            SpeechAnnouncer.SpeakCountdownAsync(
                _form.SpeechVoiceName,
                step => ShowCountdownStep(step, countdownVersion),
                () => CompleteQualifyingCountdown(resumePausedQualifier, countdownVersion),
                () => IsCountdownCurrent(countdownVersion));
        }

        private void CompleteQualifyingCountdown(bool resumePausedQualifier, int countdownVersion)
        {
            lock (_controlGate)
            {
                CompleteQualifyingCountdownCore(resumePausedQualifier, countdownVersion);
            }
        }

        private void CompleteQualifyingCountdownCore(bool resumePausedQualifier, int countdownVersion)
        {
            try
            {
                QualifyingState expectedState = resumePausedQualifier
                    ? QualifyingState.Paused
                    : QualifyingState.Ready;
                if (countdownVersion != _startCountdownVersion ||
                    _qualifying.State != expectedState || !CanRunController())
                {
                    return;
                }

                uint controllerTimestamp = GetCurrentControllerTimestamp();
                bool started = resumePausedQualifier
                    ? _qualifying.Resume(controllerTimestamp)
                    : _qualifying.Start(controllerTimestamp);
                if (started && !resumePausedQualifier && CombinedDistanceActive)
                    _race.SeedStartLineTiming(controllerTimestamp);
                if (!started)
                {
                    return;
                }

                if (!SaveRecovery("Qualifier start authorized")) return;
                _trackPowerEnabled = true;
                if (!TryWriteLine(GetTrackPowerCommand()) && !DemoLapStreamActive) return;
                PublishQualifyingStatus("Running");
                _form.SetStatusMessage(
                    $"{_qualifying.CurrentRacer} qualifying; " +
                    $"{_qualifying.DurationSeconds} seconds remaining");
                _log.Info(resumePausedQualifier
                    ? $"qualifier {_qualifying.CurrentNumber} resumed"
                    : $"qualifier {_qualifying.CurrentNumber} started");
            }
            finally
            {
                FinishStartCountdown(countdownVersion);
            }
        }

        private void CompleteStartCountdown(bool resumePausedHeat, bool manualStart, int countdownVersion)
        {
            lock (_controlGate)
            {
                CompleteStartCountdownCore(resumePausedHeat, manualStart, countdownVersion);
            }
        }

        private void CompleteStartCountdownCore(bool resumePausedHeat, bool manualStart, int countdownVersion)
        {
            try
            {
                if (countdownVersion != _startCountdownVersion || !CanRunController())
                {
                    return;
                }

                uint controllerTimestamp = GetCurrentControllerTimestamp();
                bool started = resumePausedHeat
                    ? _heatRace.Resume(controllerTimestamp)
                    : _heatRace.Start(controllerTimestamp);
                if (started && !resumePausedHeat && _heatRace.StartsAtLine)
                    _race.SeedStartLineTiming(_heatRace.TimingBaseTimestamp);

                if (!started)
                {
                    return;
                }

                if (!SaveRecovery("Heat start authorized")) return;
                _trackPowerEnabled = true;
                if (!TryWriteLine(GetTrackPowerCommand()) && !DemoLapStreamActive) return;
                PublishHeatRaceStatus("Running");
                string remaining = YATSS.FormatClock(_heatRace.GetRemaining(controllerTimestamp));
                _form.SetStatusMessage(resumePausedHeat
                    ? $"Heat resumed. Time remaining {remaining}"
                    : $"Heat {_heatRace.HeatNumber} started. Time remaining {remaining}");
                _log.Info(resumePausedHeat
                    ? "heat resumed"
                    : manualStart ? $"heat {_heatRace.HeatNumber} started manually" : $"heat {_heatRace.HeatNumber} started automatically");
            }
            finally
            {
                FinishStartCountdown(countdownVersion);
            }
        }

        private void CancelStartCountdown()
        {
            _startCountdownVersion++;
            _startCountdownInProgress = false;
            _form.HideStartCountdown();
        }

        private bool CanRunController() => EventChangesAllowed && (DemoLapStreamActive || _controllerSession.PowerAllowed);

        private bool IsCountdownCurrent(int countdownVersion)
        {
            lock (_controlGate)
            {
                return countdownVersion == _startCountdownVersion && CanRunController();
            }
        }

        private void ShowCountdownStep(int step, int countdownVersion)
        {
            _form.ShowStartCountdownStep(step, () =>
            {
                lock (_controlGate)
                {
                    return countdownVersion == _startCountdownVersion && CanRunController();
                }
            });
        }

        private void FinishStartCountdown(int countdownVersion)
        {
            if (countdownVersion == _startCountdownVersion)
            {
                _startCountdownInProgress = false;
                _form.HideStartCountdown();
            }
        }

        public void Write(string value) => WriteLine(value);

        private bool IsPortOpen()
        {
            lock (_portGate)
            {
                return _port?.IsOpen == true;
            }
        }

        public void WriteLine(string value) => TryWriteLine(value);

        private bool TryWriteLine(string value)
        {
            lock (_controlGate)
            {
                return TryWriteLineCore(value);
            }
        }

        private bool TryWriteLineCore(string value)
        {
            string commandBody = value.Split('*')[0];
            if (!_controllerSession.PowerAllowed &&
                (commandBody == "TRACK_POWER:ON" ||
                 (commandBody.StartsWith("TRACK_POWER:MASK:", StringComparison.Ordinal) &&
                  commandBody != "TRACK_POWER:MASK:00")))
            {
                _log.Warn("power-enable command blocked until controller recovery is confirmed");
                return false;
            }
            IControllerConnection? port;
            lock (_portGate)
            {
                port = _port;
            }

            if (port == null || !port.IsOpen)
            {
                _log.Warn($"serial write skipped because port is closed: {value}");
                _form.SetStatusMessage("Serial port disconnected");
                HandleControllerFault("Controller port is closed", requestPowerCut: false);
                return false;
            }

            try
            {
                string frame = value.Contains('*') ? value : LapProtocolParser.EncodeFrame(value);
                port.WriteLine(frame);
                _log.Info($"TX {frame}");
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TimeoutException)
            {
                _log.Error(ex, "serial write failed");
                _form.SetStatusMessage("Serial write failed");
                HandleControllerFault("Controller write failed", requestPowerCut: false);
                RequestReconnect();
                return false;
            }
        }

        private async Task ReadLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                if (_firmwareUpdateActive)
                {
                    await DelayDuringFirmwareUpdateAsync();
                    continue;
                }

                string portName = _form.port;
                if (string.IsNullOrWhiteSpace(portName))
                {
                    _log.Warn("no serial port configured");
                    _form.SetStatusMessage("No serial port configured");
                    await DelayReconnectAsync();
                    continue;
                }

                try
                {
                    int connectionGeneration = Volatile.Read(ref _connectionGeneration);
                    using IControllerConnection port = OpenPort(portName);
                    port.DiscardBuffers();
                    _log.Info($"serial port open on {portName}");
                    lock (_controlGate)
                    {
                        lock (_portGate)
                        {
                            _port = port;
                        }
                        _controllerSession.BeginConnection();
                        _trackPowerEnabled = false;
                        _form.SetStatusMessage($"Serial open on {portName}; verifying controller with power off");
                        WriteLine("TRACK_POWER:MASK:00");
                        WriteLine(GetSensorDebounceCommand());
                        WriteLine("PING");
                    }
                    if (_diagnosticsActive)
                    {
                        WriteLine("DIAG:START");
                    }
                    DateTime lastLineReceived = DateTime.UtcNow;
                    DateTime lastPingSent = DateTime.MinValue;
                    bool waitingForPingReply = false;

                    while (!_stop.IsCancellationRequested &&
                           !_firmwareUpdateActive &&
                           connectionGeneration == Volatile.Read(ref _connectionGeneration) &&
                           port.IsOpen)
                    {
                        string line;
                        try
                        {
                            line = port.ReadLine();
                        }
                        catch (TimeoutException)
                        {
                            if (CheckControllerResponse(portName, ref lastLineReceived, ref lastPingSent, ref waitingForPingReply))
                            {
                                break;
                            }

                            continue;
                        }
                        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is NullReferenceException)
                        {
                            if (!_firmwareUpdateActive)
                            {
                                _log.Error(ex, $"serial read failed on {portName}");
                                _form.SetStatusMessage($"Serial disconnected from {portName}");
                            }
                            break;
                        }

                        lastLineReceived = DateTime.UtcNow;
                        _lastControllerResponseUtc = lastLineReceived;
                        waitingForPingReply = false;
                        if (!_receivedLines.Writer.TryWrite((line, connectionGeneration)))
                        {
                            // Cut directly before waiting for race processing/storage.
                            lock (_portGate) port.WriteLine(LapProtocolParser.EncodeFrame("TRACK_POWER:MASK:00"));
                            HandleControllerFault("Sensor processing queue overflow; review unrecorded crossings");
                            RequestReconnect();
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is NullReferenceException || ex is TimeoutException)
                {
                    _log.Error(ex, $"serial disconnected from {portName}");
                    _form.SetStatusMessage($"Serial disconnected from {portName}");
                }
                finally
                {
                    Interlocked.Increment(ref _connectionGeneration);
                    if (!_stop.IsCancellationRequested && !_firmwareUpdateActive)
                    {
                        HandleControllerFault("Controller connection lost", requestPowerCut: false);
                    }
                    CloseActivePort();
                    lock (_controlGate)
                    {
                        _controllerSession.EndConnection();
                    }
                    Volatile.Write(ref _controllerIdentity, null);
                }

                await DelayReconnectAsync();
            }
        }

        private IControllerConnection OpenPort(string portName)
        {
            if (ControllerEndpoint.TryParseTcp(portName, out string host, out int tcpPort))
            {
                return new TcpControllerConnection(host, tcpPort, readTimeout: 1500, writeTimeout: 500);
            }

            SerialPort port = CreatePort(portName, enableControlLines: true);
            try
            {
                port.Open();
                return new SerialControllerConnection(port);
            }
            catch (IOException ex) when (PlatformEnvironment.IsWine)
            {
                port.Dispose();
                _log.Warn(
                    $"serial open with DTR/RTS failed on {portName}; " +
                    $"retrying without modem control lines ({ex.Message})");
            }

            port = CreatePort(portName, enableControlLines: false);
            try
            {
                port.Open();
                return new SerialControllerConnection(port);
            }
            catch
            {
                port.Dispose();
                throw;
            }
        }

        private static SerialPort CreatePort(string portName, bool enableControlLines)
        {
            SerialPort port = new(portName, 115200)
            {
                NewLine = "\n",
                // Heartbeats arrive once per second; allow margin for USB scheduling jitter.
                ReadTimeout = 1500,
                WriteTimeout = 500
            };

            if (enableControlLines)
            {
                port.DtrEnable = true;
                port.RtsEnable = true;
            }

            return port;
        }

        private bool CheckControllerResponse(
            string portName,
            ref DateTime lastLineReceived,
            ref DateTime lastPingSent,
            ref bool waitingForPingReply)
        {
            DateTime now = DateTime.UtcNow;
            lock (_controlGate)
            {
                if (_controllerSession.HeartbeatExpired)
                {
                    HandleControllerFault("Controller heartbeats stopped");
                }
            }
            if (waitingForPingReply && now - lastPingSent >= ControllerPingTimeout)
            {
                _log.Warn($"no controller response on {portName}");
                _form.SetStatusMessage($"No response from controller on {portName}; {FormatLastHeard()}");
                RequestReconnect();
                return true;
            }

            if (now - lastLineReceived >= ControllerPingInterval && now - lastPingSent >= ControllerPingInterval)
            {
                _form.SetStatusMessage($"Checking controller on {portName}; {FormatLastHeard()}");
                WriteLine("PING");
                lastPingSent = now;
                waitingForPingReply = true;
            }

            return false;
        }

        private void HandleLine(string line, bool isDemoLine)
        {
            lock (_controlGate)
            {
                HandleLineCore(line, isDemoLine);
            }
        }

        private void HandleLineCore(string line, bool isDemoLine)
        {
            string trimmed = line.Trim();
            _log.Raw(trimmed);
            LapProtocolMessage message = LapProtocolParser.Parse(trimmed);
            bool demoActive = DemoLapStreamActive;

            if (!isDemoLine)
            {
                if (message.Detail is "HELLO:TRACK_POWER:MASK:00" or "HELLO:TRACK_POWER:OFF")
                {
                    _controllerSession.ObservePowerOff();
                }
                if (message.Detail == "HELLO:RESETTING")
                {
                    HandleControllerFault("Controller reset detected");
                    _controllerClockResetPending = true;
                }
                if (message.Detail.StartsWith("ERR:WINDOWS_WATCHDOG:", StringComparison.Ordinal))
                {
                    HandleControllerFault("Controller watchdog cut track power");
                }
                if (message.ControllerIdentity is { ProtocolVersion: >= 2 and <= 4 } identity &&
                    identity.LaneCount >= _form.ActiveLaneCount)
                {
                    _controllerSession.ObserveIdentity();
                    Volatile.Write(ref _controllerIdentity, identity);
                }
                if (message.ControllerTimestampMillis is uint timestamp)
                {
                    bool heartbeat = message.Kind == LapProtocolMessageKind.Heartbeat;
                    bool reset = _controllerClockResetPending ||
                        (heartbeat && _controllerSession.IsBackwardTimestamp(timestamp));
                    if (reset && !_controllerClockResetPending)
                    {
                        HandleControllerFault("Controller clock restarted");
                    }
                    if (!_controllerSession.ObserveTimestamp(timestamp, heartbeat, newClock: reset))
                    {
                        _log.Warn($"ignored stale controller timestamp {timestamp}");
                        return;
                    }
                    _controllerClockResetPending = false;
                }
            }

            if (!isDemoLine && demoActive)
            {
                if (ShouldAcknowledgePhysicalControllerHeartbeatDuringDemo(
                    isDemoLine,
                    demoActive,
                    message.Kind))
                {
                    // Demo timing uses its own clock, but the physical controller still
                    // needs its keepalive so the safety watchdog does not cut power.
                    WriteLine("KEEPALIVE");
                }
                else
                {
                    _log.Info($"DEMO: ignored real serial line while demo stream is active: {trimmed}");
                }
                return;
            }

            if (isDemoLine && message.ControllerTimestampMillis.HasValue)
            {
                UpdateLatestControllerTimestamp(message.ControllerTimestampMillis.Value);
            }

            switch (message.Kind)
            {
                case LapProtocolMessageKind.Edge:
                    if (!isDemoLine && !_controllerSession.PowerAllowed)
                    {
                        _log.Info("ignored EDGE while controller recovery is pending");
                    }
                    else if (_diagnosticsActive)
                    {
                        _log.Info("ignored EDGE while controller diagnostics are active");
                    }
                    else if (message.Edge != null)
                    {
                        HandleEdge(message.Edge);
                    }
                    break;
                case LapProtocolMessageKind.Hello:
                    if (!isDemoLine && message.ControllerIdentity != null)
                    {
                        Volatile.Write(ref _controllerIdentity, message.ControllerIdentity);
                        _log.Info(
                            $"controller identity {message.ControllerIdentity.BoardProfile} " +
                            $"firmware {message.ControllerIdentity.FirmwareVersion} " +
                            $"protocol {message.ControllerIdentity.ProtocolVersion}" +
                            (message.ControllerIdentity.FlashCapacityBytes.HasValue
                                ? $" flash {message.ControllerIdentity.FlashCapacityBytes.Value} bytes"
                                : string.Empty));
                    }

                    if (message.Detail.StartsWith("HELLO:YATSSMC:", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteLine(GetSensorDebounceCommand());
                        WriteLine(GetTrackPowerCommand());
                        if (_diagnosticsActive)
                        {
                            WriteLine("DIAG:START");
                        }
                    }

                    _form.SetStatusMessage(
                        message.Detail.Contains("RESETTING", StringComparison.OrdinalIgnoreCase)
                            ? message.Detail
                            : FormatControllerRespondingStatus());
                    _log.Info(message.Detail);
                    break;
                case LapProtocolMessageKind.Diagnostic:
                    if (message.Diagnostic != null)
                    {
                        DiagnosticReceived?.Invoke(message.Diagnostic);
                        if (_diagnosticsActive &&
                            message.Diagnostic is ControllerDiagnosticSession
                            {
                                State: "STOPPED",
                                Reason: "TIMEOUT"
                            })
                        {
                            WriteLine("DIAG:START");
                        }
                    }
                    break;
                case LapProtocolMessageKind.Heartbeat:
                    if (!isDemoLine)
                    {
                        WriteLine("KEEPALIVE");
                    }

                    if (CheckQualifyingExpired(message.ControllerTimestampMillis))
                    {
                        break;
                    }

                    if (_qualifying.State != QualifyingState.Inactive)
                    {
                        PublishQualifyingStatus(GetQualifyingStateDisplayName());
                    }
                    else if (!CheckHeatExpired(message.ControllerTimestampMillis) && _heatRace.State == HeatRaceState.Practice)
                    {
                        _form.SetStatusMessage(FormatControllerRespondingStatus());
                    }
                    else if (_heatRace.State != HeatRaceState.Practice)
                    {
                        PublishHeatRaceStatus(GetCurrentHeatStatusName());
                    }
                    break;
                case LapProtocolMessageKind.Error:
                    if (message.Detail.StartsWith("ERR:WINDOWS_WATCHDOG:", StringComparison.OrdinalIgnoreCase))
                    {
                        // The fault was handled before observing the message timestamp.
                    }
                    else
                    {
                        _form.SetStatusMessage(message.Detail);
                        _log.Info(message.Detail);
                    }
                    break;
                case LapProtocolMessageKind.Ignored:
                    break;
                default:
                    _log.Warn($"rejected serial line '{message.RawLine}': {message.Detail}");
                    _form.SetStatusMessage($"Rejected serial line: {message.Detail}");
                    break;
            }
            if (!isDemoLine && _controllerSession.NeedsResume)
            {
                PublishControllerRecoveryStatus();
            }
        }

        private async Task RunDemoLapStreamAsync(CancellationToken token)
        {
            try
            {
                Random random = new(Random.Shared.Next());
                uint[] laneSequences = new uint[LapProtocolParser.LaneCount];
                uint demoTimestamp = GetDemoControllerTimestamp();
                uint nextHeartbeat = demoTimestamp + 3000;
                int[] demoLanePaceMilliseconds = DemoLapTiming.CreateLanePaces(random);
                uint[] nextLaneEdge = new uint[LapProtocolParser.LaneCount];
                string[] laneRacerAtNextEdge = new string[LapProtocolParser.LaneCount];
                HeatRaceState previousDemoHeatState = _heatRace.State;
                QualifyingState previousDemoQualifyingState = _qualifying.State;

                for (int lane = 0; lane < nextLaneEdge.Length; lane++)
                {
                    laneRacerAtNextEdge[lane] = GetDemoLaneRacerName(lane);
                    nextLaneEdge[lane] = demoTimestamp +
                        (uint)random.Next(0, 201) +
                        (uint)DemoLapTiming.GetFirstBaselineMilliseconds(
                            random,
                            _demoLapTiming.GetReferencePaceMilliseconds(
                                lane,
                                demoLanePaceMilliseconds,
                                laneRacerAtNextEdge[lane]),
                            _form.TrackLengthFeet,
                            _form.MinLapMilliseconds);
                }

                HandleDemoLine(LapProtocolParser.EncodeFrame($"HEARTBEAT:{demoTimestamp}"));
                HandleDemoLine(LapProtocolParser.EncodeFrame("HELLO:DEMO_LAP_STREAM"));

                while (!token.IsCancellationRequested)
                {
                    int activeLaneCount = Math.Clamp(_form.ActiveLaneCount, 2, LapProtocolParser.LaneCount);
                    demoTimestamp = GetDemoControllerTimestamp();
                    HeatRaceState demoHeatState = _heatRace.State;

                    if (demoHeatState != previousDemoHeatState || _qualifying.State != previousDemoQualifyingState)
                    {
                        previousDemoHeatState = demoHeatState;
                        previousDemoQualifyingState = _qualifying.State;
                        if (demoHeatState == HeatRaceState.Running || _qualifying.State == QualifyingState.Running)
                        {
                            demoTimestamp = GetDemoControllerTimestamp();
                            _log.Info($"DEMO: heat {_heatRace.HeatNumber} running at {demoTimestamp} ms");
                            for (int lane = 0; lane < activeLaneCount; lane++)
                            {
                                string currentRacer = GetDemoLaneRacerName(lane);
                                laneRacerAtNextEdge[lane] = currentRacer;
                                nextLaneEdge[lane] = demoTimestamp +
                                    (uint)random.Next(0, 201) +
                                    (uint)DemoLapTiming.GetFirstBaselineMilliseconds(
                                        random,
                                        _demoLapTiming.GetReferencePaceMilliseconds(
                                            lane,
                                            demoLanePaceMilliseconds,
                                            currentRacer),
                                        _form.TrackLengthFeet,
                                        _form.MinLapMilliseconds);
                            }
                        }
                    }

                    for (int lane = 0; lane < activeLaneCount; lane++)
                    {
                        string currentRacer = GetDemoLaneRacerName(lane);
                        if (_qualifying.State != QualifyingState.Inactive &&
                            (_qualifying.State != QualifyingState.Running || lane != _qualifying.LaneIndex)) continue;
                        if (_qualifying.State == QualifyingState.Inactive && demoHeatState != HeatRaceState.Practice &&
                            demoHeatState != HeatRaceState.Running)
                        {
                            continue;
                        }

                        if (demoHeatState != HeatRaceState.Practice &&
                            string.IsNullOrWhiteSpace(currentRacer))
                        {
                            laneRacerAtNextEdge[lane] = string.Empty;
                            nextLaneEdge[lane] = demoTimestamp + 1000;
                            continue;
                        }

                        if (!string.Equals(laneRacerAtNextEdge[lane], currentRacer, StringComparison.OrdinalIgnoreCase))
                        {
                            laneRacerAtNextEdge[lane] = currentRacer;
                            nextLaneEdge[lane] = demoTimestamp +
                                (uint)random.Next(0, 201) +
                                (uint)DemoLapTiming.GetFirstBaselineMilliseconds(
                                    random,
                                    _demoLapTiming.GetReferencePaceMilliseconds(
                                        lane,
                                        demoLanePaceMilliseconds,
                                        currentRacer),
                                    _form.TrackLengthFeet,
                                    _form.MinLapMilliseconds);
                        }

                        if (demoTimestamp < nextLaneEdge[lane])
                        {
                            continue;
                        }

                        uint edgeTimestamp = nextLaneEdge[lane];
                        string frame = LapProtocolParser.EncodeFrame(
                            $"EDGE:{lane}:{++laneSequences[lane]}:{edgeTimestamp}");
                        HandleDemoLine(frame);
                        nextLaneEdge[lane] = edgeTimestamp +
                            (uint)DemoLapTiming.GetLapIntervalMilliseconds(
                                random,
                                _demoLapTiming.GetReferencePaceMilliseconds(
                                    lane,
                                    demoLanePaceMilliseconds,
                                    currentRacer),
                                _form.TrackLengthFeet,
                                _form.MinLapMilliseconds);
                    }

                    if (demoTimestamp >= nextHeartbeat)
                    {
                        HandleDemoLine(LapProtocolParser.EncodeFrame($"HEARTBEAT:{demoTimestamp}"));
                        nextHeartbeat = demoTimestamp + 3000;
                    }

                    await Task.Delay(50, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_demoGate)
                {
                    _demoClockActive = false;
                    _demoClock = null;
                }

                _log.Info("DEMO: lap stream stopped");
                _form.SetStatusMessage("Demo lap stream stopped");
                _form.SetDemoLapStreamChecked(false);
                if (IsPortOpen())
                {
                    WriteLine(GetSensorDebounceCommand());
                    WriteLine(GetTrackPowerCommand());
                }
            }
        }

        internal static bool ShouldAcknowledgePhysicalControllerHeartbeatDuringDemo(
            bool isDemoLine,
            bool demoActive,
            LapProtocolMessageKind messageKind) =>
            !isDemoLine && demoActive && messageKind == LapProtocolMessageKind.Heartbeat;

        private void HandleControllerFault(string reason, bool requestPowerCut = true)
        {
            lock (_controlGate)
            {
                bool firstFault = !_controllerFaultActive;
                _controllerFaultActive = true;
                _trackPowerEnabled = false;
                _controllerRecoveryReason = reason;
                if (firstFault)
                {
                    if (!DemoLapStreamActive)
                    {
                        CancelStartCountdown();
                        bool intermission = _heatRace.State == HeatRaceState.Complete && _heatRace.HasMoreHeats;
                        CancelBetweenHeatsTimer();
                        _betweenHeatsPaused = intermission;
                        bool qualifierInterrupted = _controllerSession.SuspendRace(_heatRace, _qualifying, _race);
                        if (qualifierInterrupted)
                        {
                            PrepareCurrentQualifier();
                        }
                        _log.Warn($"{reason}; session interrupted at last confirmed controller time " +
                            $"{_controllerSession.LastConfirmedTimestamp} ms. Outage crossings are uncertain; " +
                            "review lap counts before resuming. Interrupted qualifying must be rerun.");
                    }
                    else
                    {
                        _controllerSession.RequireRecovery();
                    }
                    if (requestPowerCut && IsPortOpen())
                    {
                        TryWriteLine("TRACK_POWER:MASK:00");
                        TryWriteLine("PING");
                    }
                }
                else
                {
                    _controllerSession.RequireRecovery();
                }
                PublishControllerRecoveryStatus();
            }
        }

        private void PublishControllerRecoveryStatus()
        {
            if (DemoLapStreamActive)
            {
                return;
            }
            string state = _controllerSession.IsReady ? "Controller ready" : "Controller lost";
            if (_qualifying.State != QualifyingState.Inactive)
            {
                PublishQualifyingStatus(state);
            }
            else if (_heatRace.State != HeatRaceState.Practice)
            {
                PublishHeatRaceStatus(state);
            }
            else
            {
                _form.UpdateControllerStatus(state);
            }
            _form.SetStatusMessage(_controllerSession.IsReady
                ? "Controller ready - power off. Review lap counts, then press Space to continue."
                : $"{_controllerRecoveryReason} - verifying power off, controller identity, and heartbeat.");
        }

        private void InitializeDemoControllerClockCore()
        {
            _demoStartTimestamp = _hasControllerTimestamp
                ? _latestControllerTimestamp + 1000
                : 1000;
            _demoClock = Stopwatch.StartNew();
            _demoClockActive = true;
            _latestControllerTimestamp = _demoStartTimestamp;
            _hasControllerTimestamp = true;
        }

        private string GetDemoLaneRacerName(int lane)
        {
            if (QualifyingActive) return lane == _qualifying.LaneIndex ? _qualifying.CurrentRacer : string.Empty;
            if (_heatRace.State == HeatRaceState.Practice)
            {
                return string.Empty;
            }

            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetCurrentControllerTimestamp());
            return lane >= 0 && lane < snapshot.LaneRacers.Count
                ? snapshot.LaneRacers[lane].Trim()
                : string.Empty;
        }

        private void HandleDemoLine(string frame)
        {
            _log.Info($"DEMO: RX {frame}");
            HandleLine(frame, isDemoLine: true);
        }

        private string GetTrackPowerCommand()
        {
            if (!_trackPowerEnabled || !_controllerSession.PowerAllowed)
            {
                return "TRACK_POWER:MASK:00";
            }

            byte enabledLaneMask = _qualifying.State != QualifyingState.Inactive
                ? (byte)(1 << _qualifying.LaneIndex)
                : _heatRace.State == HeatRaceState.Practice
                ? (byte)((1 << _form.ActiveLaneCount) - 1)
                : _heatRace.GetOccupiedLaneMask();
            return $"TRACK_POWER:MASK:{enabledLaneMask:X2}";
        }

        private string GetSensorDebounceCommand() =>
            $"CONFIG:DEBOUNCE:{_form.SensorDebounceMilliseconds}";

        private void HandleEdge(LapEdge edge)
        {
            if (!EventChangesAllowed) return;
            if (edge.LaneIndex >= _form.ActiveLaneCount)
            {
                _log.Info($"lane {edge.LaneIndex}: ignored edge because lane is not configured");
                return;
            }

            if (_qualifying.State != QualifyingState.Inactive)
            {
                HandleQualifyingEdge(edge);
                return;
            }

            if (CheckHeatExpired(edge.TimestampMillis))
            {
                return;
            }

            LapUpdate update;
            int? previousHeatBest = null;
            if (_heatRace.State == HeatRaceState.Practice)
            {
                update = _race.Process(edge);
            }
            else
            {
                HeatRaceEdgeDecision heatDecision = _heatRace.PrepareEdge(edge);
                if (!heatDecision.ShouldProcess)
                {
                    _log.Info($"lane {edge.LaneIndex}: ignored edge because {heatDecision.Detail}");
                    return;
                }

                previousHeatBest = GetCurrentBestLapMilliseconds();

                update = _race.Process(
                    heatDecision.Edge,
                    heatDecision.CountFirstEdgeAsLap,
                    heatDecision.FastestLapEligible,
                    heatDecision.FirstLapMilliseconds);
            }

            if (!SaveRecovery("Sensor crossing")) return;
            PublishLapUpdate(edge, update, previousHeatBest);
            PublishDistanceStandings();
        }

        private void HandleQualifyingEdge(LapEdge edge)
        {
            if (CheckQualifyingExpired(edge.TimestampMillis) ||
                _qualifying.State != QualifyingState.Running)
            {
                return;
            }

            if (edge.LaneIndex != _qualifying.LaneIndex)
            {
                _log.Info($"lane {edge.LaneIndex}: ignored edge during qualifying");
                return;
            }

            LapUpdate update = _race.Process(_qualifying.AdjustEdgeTimestamp(edge));
            if (!SaveRecovery("Qualifying crossing")) return;
            PublishLapUpdate(edge, update);
        }

        private void PublishLapUpdate(
            LapEdge edge,
            LapUpdate update,
            int? previousHeatBestMilliseconds = null)
        {
            if (update.Kind == LapUpdateKind.RawIgnored)
            {
                _log.Info($"lane {edge.LaneIndex}: {update.Detail}");
                _form.SetStatusMessage($"Lane {edge.LaneIndex + 1}: {update.Detail}");
                return;
            }

            if (update.Kind == LapUpdateKind.TooFast)
            {
                if (_form.SoundOnTooFastLap)
                {
                    SystemSounds.Beep.Play();
                }

                _log.Info($"lane {edge.LaneIndex}: {update.Detail}");
                _form.SetStatusMessage($"Lane {edge.LaneIndex + 1}: {update.Detail}");
                return;
            }

            if (update.Kind == LapUpdateKind.Started || update.Kind == LapUpdateKind.Duplicate || update.Kind == LapUpdateKind.Invalid)
            {
                _log.Info($"lane {edge.LaneIndex}: {update.Detail}");
                if (update.Kind == LapUpdateKind.Started)
                {
                    _form.ShowLaneBaseline(edge.LaneIndex);
                }

                if (update.Kind == LapUpdateKind.Invalid)
                {
                    _form.SetStatusMessage($"Lane {edge.LaneIndex + 1}: {update.Detail}");
                }
                return;
            }

            int laneIndex = update.LaneIndex;
            Lane lane = _race.GetLane(laneIndex);
            if (!update.LapMilliseconds.HasValue)
            {
                _form.UpdateLaneDisplay(laneIndex, lane.getCount(), string.Empty, string.Empty, string.Empty);
                _log.Info($"lane {laneIndex}: count {lane.getCount()}, {update.Detail}");
                return;
            }

            int lapMilliseconds = update.LapMilliseconds.Value;
            string lapSeconds = FormatSeconds(lapMilliseconds);
            string bestSeconds = lane.best_time == int.MaxValue ? string.Empty : FormatSeconds(lane.best_time);
            string medianSeconds = FormatOptionalSeconds(lane.getMedian());
            _form.UpdateLaneDisplay(laneIndex, lane.getCount(), lapSeconds, bestSeconds, medianSeconds);

            LapBestSoundKind lapBestSound = LapBestSoundDecision.Select(
                _form.LapBestSoundsEnabled,
                update,
                _heatRace.State == HeatRaceState.Running,
                previousHeatBestMilliseconds);
            _lapBestSoundPlayer?.Play(lapBestSound);

            _log.Info($"lane {laneIndex}: lap {lapSeconds}s, count {lane.getCount()}, {update.Detail}");
            if (update.Kind == LapUpdateKind.MissedFrame)
            {
                _form.SetStatusMessage($"Lane {laneIndex + 1}: {update.Detail}");
            }
        }

        private int? GetCurrentBestLapMilliseconds()
        {
            int?[] bestLaps = _race.GetBestLapMilliseconds();
            return bestLaps.Where(value => value.HasValue).Min();
        }

        private uint GetControllerTimestamp()
        {
            return _controllerSession.EstimateTimestamp();
        }

        private void UpdateLatestControllerTimestamp(uint timestamp)
        {
            if (_hasControllerTimestamp)
            {
                uint delta = unchecked(timestamp - _latestControllerTimestamp);
                if (delta > int.MaxValue)
                {
                    return;
                }
            }

            _latestControllerTimestamp = timestamp;
            _hasControllerTimestamp = true;
        }

        private uint GetCurrentControllerTimestamp() =>
            DemoLapStreamActive ? GetDemoControllerTimestamp() : GetControllerTimestamp();

        private uint GetDemoControllerTimestamp()
        {
            lock (_demoGate)
            {
                if (!_demoClockActive || _demoClock == null)
                {
                    return GetControllerTimestamp();
                }

                uint timestamp = unchecked(_demoStartTimestamp + (uint)Math.Min(
                    _demoClock.ElapsedMilliseconds,
                    uint.MaxValue));
                UpdateLatestControllerTimestamp(timestamp);
                return timestamp;
            }
        }

        private bool CheckQualifyingExpired(uint? controllerTimestamp)
        {
            if ((!DemoLapStreamActive && !_controllerSession.PowerAllowed) || !controllerTimestamp.HasValue ||
                !_qualifying.IsExpired(controllerTimestamp.Value))
            {
                return false;
            }

            Lane lane = _race.GetLane(_qualifying.LaneIndex);
            int? bestLap = lane.best_time == int.MaxValue ? null : lane.best_time;
            string completedRacer = _qualifying.CurrentRacer;
            LapRaceLaneSnapshot qualifyingSnapshot = _race.GetLaneSnapshots()[_qualifying.LaneIndex];
            if (!_qualifying.CompleteCurrent(qualifyingSnapshot.Laps, controllerTimestamp.Value))
            {
                return false;
            }

            SetTrackPowerEnabled(false, null, "Qualifier complete");
            if (!SaveRecovery("Qualifier completed")) return true;
            SpeechAnnouncer.SpeakAsync(
                $"{completedRacer}, qualifying complete",
                _form.SpeechVoiceName);
            _log.Info(
                bestLap.HasValue
                    ? $"{completedRacer} qualifier complete; best {FormatSeconds(bestLap.Value)}s"
                    : $"{completedRacer} qualifier complete without a valid lap");

            if (CombinedDistanceActive)
            {
                QualifyingResult result = _qualifying.GetRankedResults().Single(item => item.RacerName == completedRacer);
                RequestQualifyingDistanceApproval(result);
            }
            else ContinueAfterQualifier();
            return true;
        }

        private void RequestQualifyingDistanceApproval(QualifyingResult result)
        {
            _distanceApprovalPending = true;
            if (!SaveRecovery("Qualifying fraction awaiting director")) return;
            int version = _raceConfigurationVersion;
            int elapsed = Math.Min(result.ElapsedMilliseconds, result.ConfiguredDurationSeconds * 1000);
            int? estimate = DistanceScoring.EstimatePartial(elapsed - (result.Laps.LastOrDefault()?.SessionElapsedMilliseconds ?? 0),
                result.Laps.Select(lap => lap.LapMilliseconds));
            _form.ShowDistanceApproval(result.RacerName, result.Laps.Count, estimate, "Qualifying", (partial, reason) =>
            {
                lock (_controlGate)
                {
                    if (version != _raceConfigurationVersion || !EventChangesAllowed) return;
                    _qualifying.ApproveLastDistance(partial, reason);
                    _distanceApprovalPending = false;
                    if (!SaveRecovery("Qualifying fraction approved")) return;
                    ContinueAfterQualifier();
                }
            }, () => version == _raceConfigurationVersion && EventChangesAllowed);
        }

        private void ContinueAfterQualifier()
        {
            if (_qualifying.State == QualifyingState.Ready)
            {
                PrepareCurrentQualifier();
                _form.SetStatusMessage(
                    $"{_qualifying.CurrentRacer} ready to qualify. Press Space to start.");
            }
            else
            {
                PublishQualifyingStatus("Complete");
                BeginQualifyingLaneSelection();
            }
        }

        private void PrepareCurrentQualifier()
        {
            _race.Reset();
            _form.ResetBoardDisplay(clearRacers: true);
            string[] names = new string[LapProtocolParser.LaneCount];
            Array.Fill(names, string.Empty);
            names[_qualifying.LaneIndex] = _qualifying.CurrentRacer;
            _form.SetLaneRacerNames(names);
            PublishQualifyingStatus("Ready");
        }

        private void BeginQualifyingLaneSelection()
        {
            if (!EventChangesAllowed) return;
            if (_qualifyingLaneSelectionPending)
            {
                return;
            }

            _qualifyingLaneSelectionPending = true;
            IReadOnlyList<QualifyingResult> rankedResults = CombinedDistanceActive ? _qualifying.GetDistanceResults() : _qualifying.GetRankedResults();
            int version = _raceConfigurationVersion;
            void ChooseLanes(IReadOnlyList<QualifyingResult> ranked)
            {
                _form.ShowQualifyingLaneSelection(ranked, (seededRacers, groups) =>
                {
                    lock (_controlGate)
                    {
                        if (version != _raceConfigurationVersion || !EventChangesAllowed) return;
                        _qualifyingResults = ranked;
                        _configuredRacers = seededRacers.ToArray();
                        _qualifying.Reset();
                        _qualifyingLaneSelectionPending = false;
                        _race.Reset();
                        _heatRace.Configure(
                            _configuredHeatLengthMinutes,
                            _configuredBetweenHeatsSeconds,
                            _configuredRacers,
                            _configuredActiveLaneCount,
                            _configuredLaneConfigurations,
                            _configuredRaceName,
                            _configuredTrackLengthFeet,
                            _qualifyingResults,
                            _configuredFormat,
                            groups.Count > 0 ? groups : null);
                        HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetCurrentControllerTimestamp());
                        if (!SaveRecovery("Qualifying lane assignments confirmed")) return;
                        _form.ResetBoardDisplay(clearRacers: false);
                        _form.SetLaneRacerNames(snapshot.LaneRacers);
                        _form.ResetHeatTimingDisplay(snapshot.LaneLapCounts);
                        PublishHeatRaceStatus("Ready");
                        SetTrackPowerEnabled(false, null, "Qualifying complete. Press Space to start Heat 1.");
                        _form.SetQualifyingAvailable(false);
                        _log.Info("qualifying lane selections complete; heat race reseeded");
                        PublishDistanceStandings();
                    }
                }, CombinedDistanceActive, () => version == _raceConfigurationVersion && EventChangesAllowed,
                    _configuredActiveLaneCount, _configuredLaneConfigurations);
            }
            if (CombinedDistanceActive)
            {
                _distanceApprovalPending = true;
                DistanceStanding[] rows = rankedResults.Select(result => new DistanceStanding(result.OriginalOrder,
                    result.RacerName, 0, string.Empty, result.Distance!.TotalHundredths, 0, null,
                    result.Distance.TotalHundredths, null, result.BestLapMilliseconds, null)).ToArray();
                _form.ResolveDistanceTies(rows, "Qualifying", ids =>
                {
                    lock (_controlGate)
                    {
                        if (version != _raceConfigurationVersion || !EventChangesAllowed) return;
                        _qualifying.SetDistanceTieOrder(ids);
                        _distanceApprovalPending = false;
                        if (!SaveRecovery("Qualifying tie order confirmed")) return;
                        ChooseLanes(_qualifying.GetDistanceResults());
                    }
                }, () => version == _raceConfigurationVersion);
            }
            else ChooseLanes(rankedResults);
        }

        private bool CheckHeatExpired(uint? controllerTimestamp)
        {
            if ((!DemoLapStreamActive && !_controllerSession.PowerAllowed) ||
                !controllerTimestamp.HasValue || !_heatRace.IsExpired(controllerTimestamp.Value))
            {
                return false;
            }

            if (_heatRace.Complete())
            {
                RecordCurrentHeatResults();
                PublishHeatRaceStatus("Complete");
                string completionSpeech = _heatRace.HasMoreHeats
                    ? $"Heat {_heatRace.HeatNumber} of {_heatRace.TotalHeats} over"
                    : "Race over";
                SetTrackPowerEnabled(false, completionSpeech, "Heat complete");
                _log.Info($"heat {_heatRace.HeatNumber} complete");
                ScheduleNextHeatIfNeeded();
            }

            return true;
        }

        private void ScheduleNextHeatIfNeeded()
        {
            if (!EventChangesAllowed) return;
            if (CombinedDistanceActive && _heatRace.IsGroupEnd && _heatRace.GetFinalDistanceCandidates().Count > 0)
            {
                ConfirmNextFinalDistance();
                return;
            }
            if (!_heatRace.HasMoreHeats)
            {
                PublishHeatRaceStatus("Race complete");
                _form.SetStatusMessage("Heat race complete");
                if (CombinedDistanceActive)
                {
                    int version = _raceConfigurationVersion;
                    _distanceApprovalPending = true;
                    _form.ResolveDistanceTies(_heatRace.GetDistanceStandings(0), "Final Results", ids =>
                    {
                        lock (_controlGate)
                        {
                            if (version != _raceConfigurationVersion || !EventChangesAllowed) return;
                            _heatRace.SetFinalTieOrder(ids);
                            if (!SaveRecovery("Final tie order confirmed")) return;
                            _distanceApprovalPending = false;
                            WriteFinalReport();
                        }
                    }, () => version == _raceConfigurationVersion && EventChangesAllowed);
                }
                else WriteFinalReport();
                return;
            }

            if (CombinedDistanceActive && _heatRace.NextHeatStartsGroup)
            {
                _form.SetStatusMessage($"Group {_heatRace.GroupNumber} complete. Place Group {_heatRace.GroupNumber + 1} at the starting line; press Space when ready.");
                return;
            }

            int betweenHeatsSeconds = _heatRace.BetweenHeatsSeconds;
            if (betweenHeatsSeconds <= 0)
            {
                PublishHeatRaceStatus("Complete");
                _form.SetStatusMessage($"Heat {_heatRace.HeatNumber} complete. Press Space for next heat. {StoppedAdjustmentHint}");
                return;
            }

            CancelBetweenHeatsTimer();
            _betweenHeatsPaused = false;
            _nextHeatStartUtc = DateTime.UtcNow.AddSeconds(betweenHeatsSeconds);
            int betweenHeatsVersion = Volatile.Read(ref _betweenHeatsVersion);
            PublishHeatRaceStatus("Intermission");
            _form.SetStatusMessage($"Heat {_heatRace.HeatNumber} complete. Next heat in {betweenHeatsSeconds} seconds. {StoppedAdjustmentHint}");
            _betweenHeatsTimer = new System.Threading.Timer(
                _ => StartNextHeatFromComplete(manualStart: false, expectedVersion: betweenHeatsVersion),
                null,
                TimeSpan.FromSeconds(betweenHeatsSeconds),
                Timeout.InfiniteTimeSpan);
            ScheduleBetweenHeatsAnnouncements(betweenHeatsSeconds, betweenHeatsVersion);
        }

        private void StartNextHeatFromComplete(bool manualStart, int? expectedVersion = null)
        {
            lock (_controlGate)
            {
                if ((expectedVersion.HasValue && expectedVersion != Volatile.Read(ref _betweenHeatsVersion)) ||
                    !CanRunController())
                {
                    return;
                }
                StartNextHeatFromCompleteCore(manualStart);
            }
        }

        private void StartNextHeatFromCompleteCore(bool manualStart)
        {
            if (!EventChangesAllowed) return;
            CancelBetweenHeatsTimer();
            _betweenHeatsPaused = false;
            RecordCurrentHeatResults();
            if (!_heatRace.PrepareNextHeat(_race.GetLapCounts()))
            {
                PublishHeatRaceStatus("Race complete");
                _form.SetStatusMessage("Heat race complete");
                WriteFinalReport();
                return;
            }

            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetCurrentControllerTimestamp());
            _race.ResetTimingForHeat(snapshot.LaneLapCounts);
            if (!SaveRecovery("Lane rotation prepared")) return;
            PublishHeatRaceStatus("Ready");
            _form.SetLaneRacerNames(snapshot.LaneRacers);
            _form.ResetHeatTimingDisplay(snapshot.LaneLapCounts);
            QueueStartCountdown(resumePausedHeat: false, manualStart);
        }

        private void CancelBetweenHeatsTimer()
        {
            Interlocked.Increment(ref _betweenHeatsVersion);
            System.Threading.Timer? timer = Interlocked.Exchange(ref _betweenHeatsTimer, null);
            timer?.Dispose();
            DisposeBetweenHeatsAnnouncementTimers();
            _nextHeatStartUtc = null;
            _betweenHeatsPaused = false;
        }

        internal static IReadOnlyList<(int RemainingSeconds, TimeSpan DueTime)> GetBetweenHeatsAnnouncements(
            int betweenHeatsSeconds)
        {
            if (betweenHeatsSeconds < 60)
            {
                return Array.Empty<(int, TimeSpan)>();
            }

            return new[] { 60, 30, 15 }
                .Select(remainingSeconds =>
                    (remainingSeconds, TimeSpan.FromSeconds(betweenHeatsSeconds - remainingSeconds)))
                .ToArray();
        }

        private void ScheduleBetweenHeatsAnnouncements(int betweenHeatsSeconds, int betweenHeatsVersion)
        {
            System.Threading.Timer[] timers = GetBetweenHeatsAnnouncements(betweenHeatsSeconds)
                .Select(announcement => new System.Threading.Timer(
                    _ => AnnounceBetweenHeatsTimeRemaining(
                        announcement.RemainingSeconds,
                        betweenHeatsVersion),
                    null,
                    announcement.DueTime,
                    Timeout.InfiniteTimeSpan))
                .ToArray();

            System.Threading.Timer[] previous = Interlocked.Exchange(
                ref _betweenHeatsAnnouncementTimers,
                timers);
            foreach (System.Threading.Timer timer in previous)
            {
                timer.Dispose();
            }
        }

        private void AnnounceBetweenHeatsTimeRemaining(int remainingSeconds, int betweenHeatsVersion)
        {
            if (betweenHeatsVersion != Volatile.Read(ref _betweenHeatsVersion) ||
                !_nextHeatStartUtc.HasValue)
            {
                return;
            }

            SpeechAnnouncer.SpeakAsync($"{remainingSeconds} seconds remaining", _form.SpeechVoiceName);
            _log.Info($"intermission announcement: {remainingSeconds} seconds remaining");
        }

        private void DisposeBetweenHeatsAnnouncementTimers()
        {
            System.Threading.Timer[] timers = Interlocked.Exchange(
                ref _betweenHeatsAnnouncementTimers,
                Array.Empty<System.Threading.Timer>());
            foreach (System.Threading.Timer timer in timers)
            {
                timer.Dispose();
            }
        }

        private bool PauseBetweenHeats()
        {
            System.Threading.Timer? timer = Interlocked.Exchange(ref _betweenHeatsTimer, null);
            if (timer == null)
            {
                return false;
            }

            Interlocked.Increment(ref _betweenHeatsVersion);
            timer.Dispose();
            DisposeBetweenHeatsAnnouncementTimers();
            _nextHeatStartUtc = null;
            _betweenHeatsPaused = true;
            PublishHeatRaceStatus("Intermission paused");
            _form.SetStatusMessage(
                $"Intermission paused after Heat {_heatRace.HeatNumber}. Press Space to start the next heat. {StoppedAdjustmentHint}");
            _log.Info($"intermission after heat {_heatRace.HeatNumber} paused manually");
            return true;
        }

        private void PublishHeatRaceStatus(string state)
        {
            if (!SaveRecovery("Heat state: " + state)) return;
            if (!DemoLapStreamActive && _controllerSession.NeedsResume)
            {
                state = _controllerSession.IsReady ? "Controller ready" : "Controller lost";
            }
            uint controllerTimestamp = GetCurrentControllerTimestamp();
            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(controllerTimestamp);
            TimeSpan remaining = state == "Intermission" && _nextHeatStartUtc.HasValue
                ? _nextHeatStartUtc.Value - DateTime.UtcNow
                : snapshot.Remaining;
            _form.UpdateHeatRaceStatus(
                snapshot.HeatNumber,
                _heatRace.TotalHeats,
                state,
                remaining,
                CombinedDistanceActive ? $"Group {_heatRace.GroupNumber}/{_heatRace.GroupCount}" : snapshot.OnDeckRacer);
            PublishDistanceStandings();
        }

        private void ClearDistanceWorkflow()
        {
            _raceConfigurationVersion++;
            _distanceApprovalPending = false;
            _configuredFormat = RaceFormat.HeatRace;
            _form.UpdateDistanceStandings(null);
        }

        private void PublishDistanceStandings()
        {
            if (!SaveRecovery("Distance state")) return;
            if (!CombinedDistanceActive || QualifyingActive || _qualifyingResults.Count == 0) return;
            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetCurrentControllerTimestamp());
            IReadOnlyList<DistanceStanding> standings = _heatRace.GetDistanceStandings(GetCurrentControllerTimestamp(), _race.GetLaneSnapshots());
            _form.ShowCombinedLaneTotals(snapshot.LaneRacers, standings);
            _form.UpdateDistanceStandings(standings);
        }

        private void ConfirmNextFinalDistance()
        {
            if (!EventChangesAllowed) return;
            var candidates = _heatRace.GetFinalDistanceCandidates();
            if (candidates.Count == 0) { _distanceApprovalPending = false; ScheduleNextHeatIfNeeded(); return; }
            var candidate = candidates[0];
            int version = _raceConfigurationVersion;
            _distanceApprovalPending = true;
            _form.SetStatusMessage("Confirm finishing fractions before continuing");
            _form.ShowDistanceApproval(candidate.RacerName, candidate.Laps, candidate.Estimate, "Race", (partial, reason) =>
            {
                lock (_controlGate)
                {
                    if (version != _raceConfigurationVersion || !EventChangesAllowed) return;
                    _heatRace.ApproveFinalDistance(candidate.RacerId, partial, reason);
                    if (!SaveRecovery("Final fraction approved")) return;
                    PublishDistanceStandings();
                    ConfirmNextFinalDistance();
                }
            }, () => version == _raceConfigurationVersion);
        }

        private void WriteFinalReport()
        {
            if (!SaveRecovery("Final results ready for export")) return;
            HeatRaceReport report = _heatRace.CreateReport();
            _raceReports.AnnouncePodium(report);
            _reportWritten = _raceReports.Write(report);
            SaveRecovery(_reportWritten ? "Final report exported" : "Final report export requires retry");
            PublishDistanceStandings();
        }

        private void PublishQualifyingStatus(string state)
        {
            if (!SaveRecovery("Qualifying state: " + state)) return;
            if (!DemoLapStreamActive && _controllerSession.NeedsResume)
            {
                state = _controllerSession.IsReady ? "Controller ready" : "Controller lost";
            }
            _form.UpdateQualifyingStatus(
                _qualifying.CurrentNumber,
                _qualifying.RacerCount,
                state,
                _qualifying.GetRemaining(GetCurrentControllerTimestamp()),
                _qualifying.CurrentRacer);
        }

        private void RecordCurrentHeatResults()
        {
            _heatRace.RecordHeatResults(_race.GetLaneSnapshots());
        }

        private string GetCurrentHeatStatusName()
        {
            if (_startCountdownInProgress)
            {
                return _heatRace.State == HeatRaceState.Paused ? "Resuming" : "Starting";
            }

            if (_betweenHeatsTimer != null)
            {
                return "Intermission";
            }

            if (_betweenHeatsPaused)
            {
                return "Intermission paused";
            }

            return _heatRace.State == HeatRaceState.Complete && !_heatRace.HasMoreHeats
                ? "Race complete"
                : GetStateDisplayName(_heatRace.State);
        }

        private static string GetStateDisplayName(HeatRaceState state) =>
            state switch
            {
                HeatRaceState.Ready => "Ready",
                HeatRaceState.Running => "Running",
                HeatRaceState.Paused => "Paused",
                HeatRaceState.Complete => "Complete",
                _ => "Practice"
            };

        private string GetQualifyingStateDisplayName()
        {
            if (_startCountdownInProgress)
            {
                return _qualifying.State == QualifyingState.Paused ? "Resuming" : "Starting";
            }

            return _qualifying.State switch
            {
                QualifyingState.Ready => "Ready",
                QualifyingState.Running => "Running",
                QualifyingState.Paused => "Paused",
                QualifyingState.Complete => "Complete",
                _ => string.Empty
            };
        }

        private static string FormatSeconds(int milliseconds) =>
            TimeSpan.FromMilliseconds(milliseconds).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

        private static string FormatOptionalSeconds(int milliseconds) =>
            milliseconds > 0 ? FormatSeconds(milliseconds) : string.Empty;

        private static string StoppedAdjustmentHint =>
            "Ctrl+1-8 add lap; Ctrl+Shift+1-8 subtract.";

        private string FormatControllerRespondingStatus() =>
            $"Controller responding on {_form.port}; {FormatLastHeard()}";

        private string FormatLastHeard()
        {
            if (!_lastControllerResponseUtc.HasValue)
            {
                return "last heard: never";
            }

            TimeSpan age = DateTime.UtcNow - _lastControllerResponseUtc.Value;
            return age.TotalSeconds < 2
                ? "last heard: now"
                : $"last heard: {Math.Round(age.TotalSeconds)}s ago";
        }

        private static void SavePort(string portName) => AppDatabase.SaveSerialPort(portName);

        private async Task DelayReconnectAsync()
        {
            try
            {
                Task reconnectNow;
                lock (_reconnectGate)
                {
                    reconnectNow = _reconnectNow.Task;
                }

                Task delay = Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);
                await Task.WhenAny(delay, reconnectNow);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task DelayDuringFirmwareUpdateAsync()
        {
            try
            {
                await Task.Delay(200, _stop.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void RequestReconnect(string? statusMessage = null)
        {
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                _form.SetStatusMessage(statusMessage);
            }

            Interlocked.Increment(ref _connectionGeneration);
            TaskCompletionSource reconnectNow;
            lock (_reconnectGate)
            {
                reconnectNow = _reconnectNow;
                _reconnectNow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            reconnectNow.TrySetResult();
        }

        private async Task WaitForPortToCloseAsync(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (IsPortOpen() && DateTime.UtcNow < deadline && !_stop.IsCancellationRequested)
            {
                await Task.Delay(25);
            }

            if (IsPortOpen())
            {
                _log.Warn("serial read did not stop before the close timeout; forcing the port closed");
                CloseActivePort();
            }
        }

        private void CloseActivePort()
        {
            lock (_portGate)
            {
                if (_port == null)
                {
                    return;
                }

                try
                {
                    if (_port.IsOpen)
                    {
                        _port.Close();
                    }
                }
                catch
                {
                }
                finally
                {
                    _port = null;
                }
            }
        }

        public void Dispose()
        {
            _recoveryTimer?.Dispose();
            StopControllerDiagnostics();
            lock (_controlGate)
            {
                CancelStartCountdown();
                CancelBetweenHeatsTimer();
                _trackPowerEnabled = false;
                SaveRecovery("Application closing; power off on recovery");
                if (IsPortOpen())
                {
                    TryWriteLine("TRACK_POWER:MASK:00");
                }
                _controllerSession.EndConnection();
            }
            StopDemoLapStream();
            _stop.Cancel();
            Interlocked.Increment(ref _connectionGeneration);
            try
            {
                _readerTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
            }

            CloseActivePort();

            _receivedLines.Writer.TryComplete();
            try { _processingTask?.Wait(TimeSpan.FromSeconds(3)); } catch { }
            lock (_controlGate) { _recoveryArmed = false; _recovery?.Dispose(); _recovery = null; }
            _lapBestSoundPlayer?.Dispose();
            _log.Dispose();
            _stop.Dispose();
        }

        private void StopDemoLapStream()
        {
            lock (_demoGate)
            {
                _demoStop?.Cancel();
            }
        }
    }
}

