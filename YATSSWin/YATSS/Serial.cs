using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Media;

namespace YATSS
{
    public sealed class Serial : IDisposable
    {
        private readonly YATSS _form;
        private readonly LapRace _race = new();
        private readonly HeatRaceController _heatRace = new();
        private readonly QualifyingController _qualifying = new();
        private readonly SerialLog _log = new();
        private readonly DemoLapTiming _demoLapTiming = new();
        private readonly RaceReportService _raceReports;
        private readonly CancellationTokenSource _stop = new();
        private readonly object _portGate = new();
        private readonly object _reconnectGate = new();
        private readonly object _demoGate = new();
        private TaskCompletionSource _reconnectNow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _readerTask;
        private Task? _demoTask;
        private SerialPort? _port;
        private CancellationTokenSource? _demoStop;
        private Stopwatch? _demoClock;
        private uint _demoStartTimestamp;
        private bool _demoClockActive;
        private System.Threading.Timer? _betweenHeatsTimer;
        private bool _betweenHeatsPaused;
        private DateTime? _nextHeatStartUtc;
        private DateTime? _lastControllerResponseUtc;
        private DateTime? _latestControllerTimestampUtc;
        private uint _latestControllerTimestamp;
        private bool _hasControllerTimestamp;
        private bool _trackPowerEnabled = true;
        private bool _diagnosticsActive;
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
        private static readonly TimeSpan ControllerPingInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ControllerPingTimeout = TimeSpan.FromSeconds(3);

        public Serial(YATSS form)
        {
            _form = form;
            _raceReports = new RaceReportService(form, _log);
            Init();
            ApplySettings();
            _readerTask = Task.Run(ReadLoopAsync);
        }

        public bool QualifyingActive => _qualifying.State != QualifyingState.Inactive;

        public event Action<ControllerDiagnostic>? DiagnosticReceived;

        public bool DiagnosticsActive => _diagnosticsActive;

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
                $"sound on too-fast laps is {_form.SoundOnTooFastLap}");
            if (_heatRace.State == HeatRaceState.Practice && IsPortOpen())
            {
                WriteLine(GetSensorDebounceCommand());
                WriteLine(GetTrackPowerCommand());
            }

            _form.SetStatusMessage($"Minimum lap time {_form.MinLapMilliseconds} ms");
        }

        public void RefreshActiveStatus()
        {
            uint controllerTimestamp = GetCurrentControllerTimestamp();
            if (CheckQualifyingExpired(controllerTimestamp))
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

            if (!CheckHeatExpired(controllerTimestamp))
            {
                PublishHeatRaceStatus(GetCurrentHeatStatusName());
            }
        }

        public void SetPort(string portName)
        {
            portName = portName.Trim();
            if (string.Equals(_form.port, portName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _form.port = portName;
            SavePort(portName);
            _log.Info(string.IsNullOrWhiteSpace(portName) ? "serial port cleared" : $"serial port set to {portName}");
            _form.SetStatusMessage(string.IsNullOrWhiteSpace(portName) ? "No serial port configured" : $"Serial port set to {portName}");
            RequestReconnect();
        }

        public void Init()
        {
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
            Init();
            if (resetArduino)
            {
                WriteLine("RESET");
                RequestReconnect("Waiting for controller after reset");
            }
        }

        public void ResetLane(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= LapProtocolParser.LaneCount)
            {
                return;
            }

            _race.ResetLane(laneIndex);
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
            double trackLengthFeet)
        {
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
                _qualifyingResults);
            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetCurrentControllerTimestamp());
            _form.ResetHeatTimingDisplay(snapshot.LaneLapCounts);
            PublishHeatRaceStatus("Ready");
            SetTrackPowerEnabled(false, null, $"Heat 1 ready: {heatLengthMinutes} minute heat. Press Space to start.");
            _log.Info($"heat race configured for {heatLengthMinutes} minute(s), {betweenHeatsSeconds} second(s) between heats");
        }

        public void SetPracticeMode()
        {
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
                    _demoStop?.Cancel();
                    _form.SetStatusMessage("Demo lap stream stopping");
                    return false;
                }

                _demoStop?.Dispose();
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
            if (_diagnosticsActive)
            {
                _form.SetStatusMessage("Close Controller Diagnostics before using race controls");
                return;
            }

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
                _qualifyingResults);
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
            if (laneIndex < 0 || laneIndex >= _form.ActiveLaneCount)
            {
                _form.SetStatusMessage($"Lane {laneIndex + 1} is not configured");
                return false;
            }

            if (!_heatRace.CanAdjustLapCounts)
            {
                _form.SetStatusMessage("Lap adjustment is only available during stopped heat time");
                return false;
            }

            int previousCount = _race.GetLane(laneIndex).getCount();
            int count = _race.AdjustLapCount(laneIndex, delta);
            _heatRace.RecordManualLapAdjustment(laneIndex, count - previousCount, count);
            Lane lane = _race.GetLane(laneIndex);
            string bestSeconds = lane.best_time == int.MaxValue ? string.Empty : FormatSeconds(lane.best_time);
            string medianSeconds = FormatOptionalSeconds(lane.getMedian());
            _form.UpdateLaneDisplay(laneIndex, count, string.Empty, bestSeconds, medianSeconds);
            RecordCurrentHeatResults();

            string direction = delta > 0 ? "added to" : "subtracted from";
            _log.Info($"lane {laneIndex}: manual lap {direction} stopped heat, count {count}");
            _form.SetStatusMessage($"{StoppedAdjustmentHint} Lane {laneIndex + 1}: manual lap {direction} total, count {count}");
            return true;
        }

        private void SetTrackPowerEnabled(bool enabled, string? speech, string statusMessage)
        {
            _trackPowerEnabled = enabled;
            string command = GetTrackPowerCommand();
            bool restoreAfterCountdown = enabled &&
                string.Equals(speech, "Let's go", StringComparison.OrdinalIgnoreCase);

            if (restoreAfterCountdown)
            {
                SpeechAnnouncer.SpeakCountdownAsync(_form.SpeechVoiceName, () => WriteLine(command));
            }
            else
            {
                WriteLine(command);
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

            if (!IsPortOpen())
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
            if (_startCountdownInProgress)
            {
                return;
            }

            _startCountdownInProgress = true;
            int countdownVersion = ++_startCountdownVersion;
            _trackPowerEnabled = true;
            _form.SetQualifyingAvailable(false);
            PublishHeatRaceStatus(resumePausedHeat ? "Resuming" : "Starting");
            _form.SetStatusMessage(resumePausedHeat ? "Heat restart countdown" : $"Heat {_heatRace.HeatNumber} countdown");
            _log.Info(resumePausedHeat ? "heat restart countdown queued" : $"heat {_heatRace.HeatNumber} start countdown queued");
            SpeechAnnouncer.SpeakCountdownAsync(_form.SpeechVoiceName, () => CompleteStartCountdown(resumePausedHeat, manualStart, countdownVersion));
        }

        private void QueueQualifyingCountdown(bool resumePausedQualifier)
        {
            QualifyingState expectedState = resumePausedQualifier
                ? QualifyingState.Paused
                : QualifyingState.Ready;
            if (_startCountdownInProgress || _qualifying.State != expectedState)
            {
                return;
            }

            _startCountdownInProgress = true;
            int countdownVersion = ++_startCountdownVersion;
            _trackPowerEnabled = true;
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
                () => CompleteQualifyingCountdown(resumePausedQualifier, countdownVersion));
        }

        private void CompleteQualifyingCountdown(bool resumePausedQualifier, int countdownVersion)
        {
            try
            {
                QualifyingState expectedState = resumePausedQualifier
                    ? QualifyingState.Paused
                    : QualifyingState.Ready;
                if (countdownVersion != _startCountdownVersion ||
                    _qualifying.State != expectedState)
                {
                    return;
                }

                WriteLine(GetTrackPowerCommand());
                uint controllerTimestamp = GetControllerTimestamp();
                bool started = resumePausedQualifier
                    ? _qualifying.Resume(controllerTimestamp)
                    : _qualifying.Start(controllerTimestamp);
                if (!started)
                {
                    return;
                }

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
                _startCountdownInProgress = false;
            }
        }

        private void CompleteStartCountdown(bool resumePausedHeat, bool manualStart, int countdownVersion)
        {
            try
            {
                if (countdownVersion != _startCountdownVersion)
                {
                    return;
                }

                WriteLine(GetTrackPowerCommand());
                uint controllerTimestamp = GetCurrentControllerTimestamp();
                bool started = resumePausedHeat
                    ? _heatRace.Resume(controllerTimestamp)
                    : _heatRace.Start(controllerTimestamp);

                if (!started)
                {
                    return;
                }

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
                _startCountdownInProgress = false;
            }
        }

        private void CancelStartCountdown()
        {
            _startCountdownVersion++;
            _startCountdownInProgress = false;
        }

        public void Write(string value) => WriteLine(value);

        private bool IsPortOpen()
        {
            lock (_portGate)
            {
                return _port?.IsOpen == true;
            }
        }

        public void WriteLine(string value)
        {
            SerialPort? port;
            lock (_portGate)
            {
                port = _port;
            }

            if (port == null || !port.IsOpen)
            {
                _log.Warn($"serial write skipped because port is closed: {value}");
                _form.SetStatusMessage("Serial port disconnected");
                return;
            }

            try
            {
                string frame = value.Contains('*') ? value : LapProtocolParser.EncodeFrame(value);
                port.WriteLine(frame);
                _log.Info($"TX {frame}");
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TimeoutException)
            {
                _log.Error(ex, "serial write failed");
                _form.SetStatusMessage("Serial write failed");
                RequestReconnect();
            }
        }

        private async Task ReadLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
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
                    using SerialPort port = CreatePort(portName);
                    port.Open();
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();
                    lock (_portGate)
                    {
                        _port = port;
                    }

                    _log.Info($"serial port open on {portName}");
                    _form.SetStatusMessage($"Serial open on {portName}; waiting for controller");
                    WriteLine(GetSensorDebounceCommand());
                    WriteLine(GetTrackPowerCommand());
                    if (_diagnosticsActive)
                    {
                        WriteLine("DIAG:START");
                    }
                    DateTime lastLineReceived = DateTime.UtcNow;
                    DateTime lastPingSent = DateTime.MinValue;
                    bool waitingForPingReply = false;

                    while (!_stop.IsCancellationRequested && port.IsOpen)
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
                            _log.Error(ex, $"serial read failed on {portName}");
                            _form.SetStatusMessage($"Serial disconnected from {portName}");
                            break;
                        }

                        lastLineReceived = DateTime.UtcNow;
                        _lastControllerResponseUtc = lastLineReceived;
                        waitingForPingReply = false;
                        HandleLine(line, isDemoLine: false);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is NullReferenceException)
                {
                    _log.Error(ex, $"serial disconnected from {portName}");
                    _form.SetStatusMessage($"Serial disconnected from {portName}");
                }
                finally
                {
                    CloseActivePort();
                }

                await DelayReconnectAsync();
            }
        }

        private static SerialPort CreatePort(string portName) =>
            new(portName, 115200)
            {
                NewLine = "\n",
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

        private bool CheckControllerResponse(
            string portName,
            ref DateTime lastLineReceived,
            ref DateTime lastPingSent,
            ref bool waitingForPingReply)
        {
            DateTime now = DateTime.UtcNow;
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
            string trimmed = line.Trim();
            _log.Raw(trimmed);

            if (!isDemoLine && DemoLapStreamActive)
            {
                _log.Info($"DEMO: ignored real serial line while demo stream is active: {trimmed}");
                return;
            }

            LapProtocolMessage message = LapProtocolParser.Parse(trimmed);
            if (message.ControllerTimestampMillis.HasValue)
            {
                UpdateLatestControllerTimestamp(message.ControllerTimestampMillis.Value);
            }

            switch (message.Kind)
            {
                case LapProtocolMessageKind.Edge:
                    if (_diagnosticsActive)
                    {
                        _log.Info("ignored EDGE while controller diagnostics are active");
                    }
                    else if (message.Edge != null)
                    {
                        HandleEdge(message.Edge);
                    }
                    break;
                case LapProtocolMessageKind.Hello:
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
                        HandleControllerWatchdogTrip(message.ControllerTimestampMillis);
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

                    if (demoHeatState != previousDemoHeatState)
                    {
                        previousDemoHeatState = demoHeatState;
                        if (demoHeatState == HeatRaceState.Running)
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
                        if (demoHeatState != HeatRaceState.Practice &&
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

        private void HandleControllerWatchdogTrip(uint? controllerTimestamp)
        {
            CancelStartCountdown();
            _trackPowerEnabled = false;
            uint timestamp = controllerTimestamp ?? GetCurrentControllerTimestamp();
            string statusMessage;

            if (_heatRace.State == HeatRaceState.Running && _heatRace.Pause(timestamp))
            {
                PublishHeatRaceStatus("Paused");
                statusMessage = "Controller watchdog cut track power and paused the heat. Press Space to restart.";
            }
            else if (_qualifying.InterruptCurrent())
            {
                PrepareCurrentQualifier();
                statusMessage = $"Controller watchdog interrupted {_qualifying.CurrentRacer}. Press Space to restart qualifying.";
            }
            else
            {
                statusMessage = "Controller watchdog cut track power. Press Space to restore practice power.";
            }

            _log.Warn(statusMessage);
            _form.SetStatusMessage(statusMessage);
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
            if (!_trackPowerEnabled)
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

                update = _race.Process(
                    heatDecision.Edge,
                    heatDecision.CountFirstEdgeAsLap,
                    heatDecision.FastestLapEligible,
                    heatDecision.FirstLapMilliseconds);
            }

            PublishLapUpdate(edge, update);
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

            PublishLapUpdate(edge, _race.Process(_qualifying.AdjustEdgeTimestamp(edge)));
        }

        private void PublishLapUpdate(LapEdge edge, LapUpdate update)
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

            _log.Info($"lane {laneIndex}: lap {lapSeconds}s, count {lane.getCount()}, {update.Detail}");
            if (update.Kind == LapUpdateKind.MissedFrame)
            {
                _form.SetStatusMessage($"Lane {laneIndex + 1}: {update.Detail}");
            }
        }

        private uint GetControllerTimestamp()
        {
            if (!_hasControllerTimestamp)
            {
                return 0;
            }

            if (!_latestControllerTimestampUtc.HasValue)
            {
                return _latestControllerTimestamp;
            }

            double elapsedMilliseconds = (DateTime.UtcNow - _latestControllerTimestampUtc.Value).TotalMilliseconds;
            if (elapsedMilliseconds <= 0)
            {
                return _latestControllerTimestamp;
            }

            uint elapsed = (uint)Math.Min(elapsedMilliseconds, uint.MaxValue);
            return unchecked(_latestControllerTimestamp + elapsed);
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
            _latestControllerTimestampUtc = DateTime.UtcNow;
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
            if (!controllerTimestamp.HasValue ||
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
            SpeechAnnouncer.SpeakAsync(
                $"{completedRacer}, qualifying complete",
                _form.SpeechVoiceName);
            _log.Info(
                bestLap.HasValue
                    ? $"{completedRacer} qualifier complete; best {FormatSeconds(bestLap.Value)}s"
                    : $"{completedRacer} qualifier complete without a valid lap");

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

            return true;
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
            if (_qualifyingLaneSelectionPending)
            {
                return;
            }

            _qualifyingLaneSelectionPending = true;
            IReadOnlyList<QualifyingResult> rankedResults = _qualifying.GetRankedResults();
            _form.ShowQualifyingLaneSelection(rankedResults, seededRacers =>
            {
                _qualifyingResults = rankedResults;
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
                    _qualifyingResults);
                HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetControllerTimestamp());
                _form.ResetBoardDisplay(clearRacers: false);
                _form.SetLaneRacerNames(snapshot.LaneRacers);
                _form.ResetHeatTimingDisplay(snapshot.LaneLapCounts);
                PublishHeatRaceStatus("Ready");
                SetTrackPowerEnabled(false, null, "Qualifying complete. Press Space to start Heat 1.");
                _form.SetQualifyingAvailable(false);
                _log.Info("qualifying lane selections complete; heat race reseeded");
            });
        }

        private bool CheckHeatExpired(uint? controllerTimestamp)
        {
            if (!controllerTimestamp.HasValue || !_heatRace.IsExpired(controllerTimestamp.Value))
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
            if (!_heatRace.HasMoreHeats)
            {
                PublishHeatRaceStatus("Race complete");
                _form.SetStatusMessage("Heat race complete");
                HeatRaceReport report = _heatRace.CreateReport();
                _raceReports.AnnouncePodium(report);
                _raceReports.Write(report);
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
            PublishHeatRaceStatus("Intermission");
            _form.SetStatusMessage($"Heat {_heatRace.HeatNumber} complete. Next heat in {betweenHeatsSeconds} seconds. {StoppedAdjustmentHint}");
            _betweenHeatsTimer = new System.Threading.Timer(
                _ => StartNextHeatFromComplete(manualStart: false),
                null,
                TimeSpan.FromSeconds(betweenHeatsSeconds),
                Timeout.InfiniteTimeSpan);
        }

        private void StartNextHeatFromComplete(bool manualStart)
        {
            CancelBetweenHeatsTimer();
            _betweenHeatsPaused = false;
            RecordCurrentHeatResults();
            if (!_heatRace.PrepareNextHeat(_race.GetLapCounts()))
            {
                PublishHeatRaceStatus("Race complete");
                _form.SetStatusMessage("Heat race complete");
                _raceReports.Write(_heatRace.CreateReport());
                return;
            }

            PublishHeatRaceStatus("Ready");
            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetCurrentControllerTimestamp());
            _race.ResetTimingForHeat(snapshot.LaneLapCounts);
            _form.SetLaneRacerNames(snapshot.LaneRacers);
            _form.ResetHeatTimingDisplay(snapshot.LaneLapCounts);
            QueueStartCountdown(resumePausedHeat: false, manualStart);
        }

        private void CancelBetweenHeatsTimer()
        {
            System.Threading.Timer? timer = Interlocked.Exchange(ref _betweenHeatsTimer, null);
            timer?.Dispose();
            _nextHeatStartUtc = null;
            _betweenHeatsPaused = false;
        }

        private bool PauseBetweenHeats()
        {
            System.Threading.Timer? timer = Interlocked.Exchange(ref _betweenHeatsTimer, null);
            if (timer == null)
            {
                return false;
            }

            timer.Dispose();
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
                snapshot.OnDeckRacer);
        }

        private void PublishQualifyingStatus(string state)
        {
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

        private void RequestReconnect(string? statusMessage = null)
        {
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                _form.SetStatusMessage(statusMessage);
            }

            CloseActivePort();
            TaskCompletionSource reconnectNow;
            lock (_reconnectGate)
            {
                reconnectNow = _reconnectNow;
                _reconnectNow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            reconnectNow.TrySetResult();
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
            StopControllerDiagnostics();
            StopDemoLapStream();
            _stop.Cancel();
            CancelBetweenHeatsTimer();
            CloseActivePort();
            try
            {
                _readerTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
            }

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

