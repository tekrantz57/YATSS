using System.Globalization;
using System.IO.Ports;
using System.Media;

namespace tlp
{
    public sealed class Serial : IDisposable
    {
        private readonly MKTS _form;
        private readonly LapRace _race = new();
        private readonly HeatRaceController _heatRace = new();
        private readonly QualifyingController _qualifying = new();
        private readonly SerialLog _log = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly object _portGate = new();
        private readonly object _reconnectGate = new();
        private readonly object _demoGate = new();
        private TaskCompletionSource _reconnectNow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _readerTask;
        private Task? _demoTask;
        private SerialPort? _port;
        private CancellationTokenSource? _demoStop;
        private System.Threading.Timer? _betweenHeatsTimer;
        private DateTime? _nextHeatStartUtc;
        private DateTime? _lastControllerResponseUtc;
        private uint _latestControllerTimestamp;
        private bool _hasControllerTimestamp;
        private bool _trackPowerEnabled = true;
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
        private const double DemoReferenceTrackLengthFeet = 155.0;
        private const int DemoReferenceMinimumLapMilliseconds = 4200;
        private const int DemoReferenceMaximumLapMilliseconds = 6500;
        private static readonly int[] DemoReferenceLanePaceMilliseconds =
        {
            4300,
            4550,
            4800,
            5050,
            5300,
            5550,
            5800,
            6050
        };

        private static readonly TimeSpan ControllerPingInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ControllerPingTimeout = TimeSpan.FromSeconds(3);

        public Serial(MKTS form)
        {
            _form = form;
            Init();
            ApplySettings();
            _readerTask = Task.Run(ReadLoopAsync);
        }

        public bool QualifyingActive => _qualifying.State != QualifyingState.Inactive;

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
            _heatRace.Configure(
                heatLengthMinutes,
                betweenHeatsSeconds,
                racers,
                activeLaneCount,
                laneConfigurations,
                raceName,
                trackLengthFeet,
                _qualifyingResults);
            PublishHeatRaceStatus("Ready");
            SetTrackPowerEnabled(false, null, $"Heat 1 ready: {heatLengthMinutes} minute heat. Press Space to start.");
            _log.Info($"heat race configured for {heatLengthMinutes} minute(s), {betweenHeatsSeconds} second(s) between heats");
        }

        public void SetPracticeMode()
        {
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
                _demoTask = Task.Run(() => RunDemoLapStreamAsync(_demoStop.Token));
                _form.SetStatusMessage("Demo lap stream started");
                _log.Info("DEMO: lap stream started");
                return true;
            }
        }

        public void HandleSpaceBar()
        {
            uint controllerTimestamp = GetControllerTimestamp();
            switch (_qualifying.State)
            {
                case QualifyingState.Ready:
                    QueueQualifyingCountdown();
                    return;
                case QualifyingState.Running:
                    _form.SetStatusMessage("Track calls are not available during qualifying");
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
                    StartNextHeatFromComplete(manualStart: true);
                    break;
                default:
                    SetTrackPowerEnabled(!_trackPowerEnabled);
                    break;
            }
        }

        public void ConfigureQualifying(int laneIndex, int durationSeconds)
        {
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
            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetControllerTimestamp());
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

            int count = _race.AdjustLapCount(laneIndex, delta);
            Lane lane = _race.GetLane(laneIndex);
            string bestSeconds = lane.best_time == int.MaxValue ? string.Empty : FormatSeconds(lane.best_time);
            string medianSeconds = FormatOptionalSeconds(lane.getMedian());
            _form.UpdateLaneDisplay(laneIndex, count, string.Empty, bestSeconds, medianSeconds, string.Empty);
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
            _form.SetStatusMessage(resumePausedHeat ? "Heat restart countdown" : $"Heat {_heatRace.HeatNumber} countdown");
            _log.Info(resumePausedHeat ? "heat restart countdown queued" : $"heat {_heatRace.HeatNumber} start countdown queued");
            SpeechAnnouncer.SpeakCountdownAsync(_form.SpeechVoiceName, () => CompleteStartCountdown(resumePausedHeat, manualStart, countdownVersion));
        }

        private void QueueQualifyingCountdown()
        {
            if (_startCountdownInProgress || _qualifying.State != QualifyingState.Ready)
            {
                return;
            }

            _startCountdownInProgress = true;
            int countdownVersion = ++_startCountdownVersion;
            _trackPowerEnabled = true;
            _form.SetQualifyingAvailable(false);
            _form.SetStatusMessage(
                $"Qualifier {_qualifying.CurrentNumber}/{_qualifying.RacerCount} countdown");
            _log.Info($"qualifier {_qualifying.CurrentNumber} start countdown queued");
            SpeechAnnouncer.SpeakCountdownAsync(
                _form.SpeechVoiceName,
                () => CompleteQualifyingCountdown(countdownVersion));
        }

        private void CompleteQualifyingCountdown(int countdownVersion)
        {
            try
            {
                if (countdownVersion != _startCountdownVersion ||
                    _qualifying.State != QualifyingState.Ready)
                {
                    return;
                }

                WriteLine(GetTrackPowerCommand());
                uint controllerTimestamp = GetControllerTimestamp();
                if (!_qualifying.Start(controllerTimestamp))
                {
                    return;
                }

                PublishQualifyingStatus("Running");
                _form.SetStatusMessage(
                    $"{_qualifying.CurrentRacer} qualifying; " +
                    $"{_qualifying.DurationSeconds} seconds remaining");
                _log.Info($"qualifier {_qualifying.CurrentNumber} started");
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
                uint controllerTimestamp = GetControllerTimestamp();
                bool started = resumePausedHeat
                    ? _heatRace.Resume(controllerTimestamp)
                    : _heatRace.Start(controllerTimestamp);

                if (!started)
                {
                    return;
                }

                PublishHeatRaceStatus("Running");
                _form.SetStatusMessage(resumePausedHeat
                    ? $"Heat resumed. Time remaining {_heatRace.GetRemaining(controllerTimestamp):m\\:ss}"
                    : $"Heat {_heatRace.HeatNumber} started. Time remaining {_heatRace.GetRemaining(controllerTimestamp):m\\:ss}");
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
                        HandleLine(line);
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

        private void HandleLine(string line)
        {
            string trimmed = line.Trim();
            _log.Raw(trimmed);

            LapProtocolMessage message = LapProtocolParser.Parse(trimmed);
            if (message.ControllerTimestampMillis.HasValue)
            {
                _latestControllerTimestamp = message.ControllerTimestampMillis.Value;
                _hasControllerTimestamp = true;
            }

            switch (message.Kind)
            {
                case LapProtocolMessageKind.Edge:
                    if (message.Edge != null)
                    {
                        HandleEdge(message.Edge);
                    }
                    break;
                case LapProtocolMessageKind.Hello:
                    if (message.Detail.StartsWith("HELLO:LAPS_REDUX:", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteLine(GetSensorDebounceCommand());
                        WriteLine(GetTrackPowerCommand());
                    }

                    _form.SetStatusMessage(
                        message.Detail.Contains("RESETTING", StringComparison.OrdinalIgnoreCase)
                            ? message.Detail
                            : FormatControllerRespondingStatus());
                    _log.Info(message.Detail);
                    break;
                case LapProtocolMessageKind.Heartbeat:
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
                    _form.SetStatusMessage(message.Detail);
                    _log.Info(message.Detail);
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
                const int demoClockStepMilliseconds = 50;
                Random random = new(20260622);
                uint sequence = 0;
                uint demoTimestamp = _hasControllerTimestamp ? _latestControllerTimestamp + 1000 : 1000;
                uint nextHeartbeat = demoTimestamp + 3000;
                uint[] nextLaneEdge = new uint[LapProtocolParser.LaneCount];

                for (int lane = 0; lane < nextLaneEdge.Length; lane++)
                {
                    nextLaneEdge[lane] = demoTimestamp + (uint)(lane * 325);
                }

                HandleDemoLine(LapProtocolParser.EncodeFrame("HELLO:DEMO_LAP_STREAM"));

                while (!token.IsCancellationRequested)
                {
                    int activeLaneCount = Math.Clamp(_form.ActiveLaneCount, 2, LapProtocolParser.LaneCount);
                    demoTimestamp += demoClockStepMilliseconds;

                    for (int lane = 0; lane < activeLaneCount; lane++)
                    {
                        if (demoTimestamp < nextLaneEdge[lane])
                        {
                            continue;
                        }

                        uint edgeTimestamp = nextLaneEdge[lane];
                        string frame = LapProtocolParser.EncodeFrame(
                            $"EDGE:{lane}:{++sequence}:{edgeTimestamp}");
                        HandleDemoLine(frame);
                        nextLaneEdge[lane] = edgeTimestamp +
                            (uint)GetDemoLapIntervalMilliseconds(random, lane, _form.TrackLengthFeet);
                    }

                    if (demoTimestamp >= nextHeartbeat)
                    {
                        HandleDemoLine(LapProtocolParser.EncodeFrame($"HEARTBEAT:{demoTimestamp}"));
                        nextHeartbeat = demoTimestamp + 3000;
                    }

                    await Task.Delay(100, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _log.Info("DEMO: lap stream stopped");
                _form.SetStatusMessage("Demo lap stream stopped");
                _form.SetDemoLapStreamChecked(false);
            }
        }

        private static int GetDemoLapIntervalMilliseconds(Random random, int lane, double trackLengthFeet)
        {
            double trackScale = Math.Clamp(trackLengthFeet, 1.0, 10000.0) / DemoReferenceTrackLengthFeet;
            int minimumLap = ScaleDemoMilliseconds(DemoReferenceMinimumLapMilliseconds, trackScale);
            int maximumLap = ScaleDemoMilliseconds(DemoReferenceMaximumLapMilliseconds, trackScale);
            int baseLap = ScaleDemoMilliseconds(
                DemoReferenceLanePaceMilliseconds[lane % DemoReferenceLanePaceMilliseconds.Length],
                trackScale);
            int interval = baseLap + ScaleDemoMilliseconds(random.Next(-280, 341), trackScale);

            if (random.NextDouble() < 0.14)
            {
                interval -= ScaleDemoMilliseconds(random.Next(120, 281), trackScale);
            }

            if (random.NextDouble() < 0.18)
            {
                interval += ScaleDemoMilliseconds(random.Next(250, 651), trackScale);
            }

            return Math.Clamp(interval, minimumLap, maximumLap);
        }

        private static int ScaleDemoMilliseconds(int referenceMilliseconds, double trackScale) =>
            Math.Max(1, (int)Math.Round(referenceMilliseconds * trackScale));

        private void HandleDemoLine(string frame)
        {
            _log.Info($"DEMO: RX {frame}");
            HandleLine(frame);
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

            PublishLapUpdate(edge, _race.Process(edge));
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
                _form.UpdateLaneDisplay(laneIndex, lane.getCount(), string.Empty, string.Empty, string.Empty, string.Empty);
                _log.Info($"lane {laneIndex}: count {lane.getCount()}, {update.Detail}");
                _form.SetStatusMessage($"Lane {laneIndex + 1}: lap counted");
                return;
            }

            int lapMilliseconds = update.LapMilliseconds.Value;
            string lapSeconds = FormatSeconds(lapMilliseconds);
            string bestSeconds = lane.best_time == int.MaxValue ? string.Empty : FormatSeconds(lane.best_time);
            string medianSeconds = FormatOptionalSeconds(lane.getMedian());
            string mph = _race.CalculateMilesPerHour(lapMilliseconds).ToString("F3", CultureInfo.InvariantCulture);

            _form.UpdateLaneDisplay(laneIndex, lane.getCount(), lapSeconds, bestSeconds, medianSeconds, mph);

            _log.Info($"lane {laneIndex}: lap {lapSeconds}s, count {lane.getCount()}, {update.Detail}");
            _form.SetStatusMessage($"Lane {laneIndex + 1}: lap {lapSeconds}s");
        }

        private uint GetControllerTimestamp() =>
            _hasControllerTimestamp ? _latestControllerTimestamp : 0;

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
            if (!_qualifying.CompleteCurrent(bestLap))
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
                AnnouncePodium(report);
                WriteHeatRaceReport(report);
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
            RecordCurrentHeatResults();
            if (!_heatRace.PrepareNextHeat(_race.GetLapCounts()))
            {
                PublishHeatRaceStatus("Race complete");
                _form.SetStatusMessage("Heat race complete");
                WriteHeatRaceReport();
                return;
            }

            PublishHeatRaceStatus("Ready");
            HeatRaceSnapshot snapshot = _heatRace.GetSnapshot(GetControllerTimestamp());
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
        }

        private void PublishHeatRaceStatus(string state)
        {
            uint controllerTimestamp = GetControllerTimestamp();
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
                _qualifying.GetRemaining(GetControllerTimestamp()),
                _qualifying.CurrentRacer);
        }

        private void RecordCurrentHeatResults()
        {
            _heatRace.RecordHeatResults(_race.GetLapCounts(), _race.GetBestLapMilliseconds());
        }

        private void AnnouncePodium(HeatRaceReport report)
        {
            string[] placeNames = { "First", "Second", "Third" };
            string announcement = string.Join(
                ". ",
                report.Racers
                    .Take(placeNames.Length)
                    .Select((racer, index) => $"{placeNames[index]} place, {racer.RacerName}"));
            if (!string.IsNullOrWhiteSpace(announcement))
            {
                SpeechAnnouncer.SpeakAsync(announcement, _form.SpeechVoiceName);
            }
        }

        private void WriteHeatRaceReport(HeatRaceReport? report = null)
        {
            try
            {
                string path = HeatRaceReportWriter.Write(report ?? _heatRace.CreateReport());
                HeatRaceReportWriter.Open(path);
                _log.Info($"heat race report written to {path}");
                _form.SetStatusMessage($"Heat race report written: {path}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                _log.Error(ex, "heat race report failed");
                _form.SetStatusMessage("Heat race report could not be written");
            }
        }

        private string GetCurrentHeatStatusName()
        {
            if (_betweenHeatsTimer != null)
            {
                return "Intermission";
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

        private string GetQualifyingStateDisplayName() =>
            _qualifying.State switch
            {
                QualifyingState.Ready => "Ready",
                QualifyingState.Running => "Running",
                QualifyingState.Complete => "Complete",
                _ => string.Empty
            };

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

