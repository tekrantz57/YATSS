using System.Globalization;
using System.IO.Ports;
using System.Media;
using Microsoft.Data.Sqlite;

namespace tlp
{
    public sealed class Serial : IDisposable
    {
        private readonly MKTS _form;
        private readonly LapRace _race = new();
        private readonly SerialLog _log = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly object _portGate = new();
        private readonly object _reconnectGate = new();
        private TaskCompletionSource _reconnectNow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _readerTask;
        private SerialPort? _port;
        private static readonly TimeSpan ControllerPingInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ControllerPingTimeout = TimeSpan.FromSeconds(3);

        public Serial(MKTS form)
        {
            _form = form;
            Init();
            ApplySettings();
            _readerTask = Task.Run(ReadLoopAsync);
        }

        public void ApplySettings()
        {
            LapRaceOptions options = _race.Options;
            _race.SetOptions(options with { MinLapMilliseconds = _form.MinLapMilliseconds });
            _log.Info($"minimum lap time set to {_form.MinLapMilliseconds} ms; sound on too-fast laps is {_form.SoundOnTooFastLap}");
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
            _race.Reset();
            _form.ResetBoardDisplay(clearRacers: true);
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
            string command = enabled ? "TRACK_POWER:ON" : "TRACK_POWER:OFF";
            WriteLine(command);
            SpeechAnnouncer.SpeakAsync(enabled ? "Let's go" : "Track call");
            _log.Info(enabled ? "track power restore requested" : "track power cut requested");
            _form.SetStatusMessage(enabled ? "Track power restore requested" : "Track power cut requested");
        }

        public void Write(string value) => WriteLine(value);

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
                _form.SetStatusMessage($"No response from controller on {portName}");
                RequestReconnect();
                return true;
            }

            if (now - lastLineReceived >= ControllerPingInterval && now - lastPingSent >= ControllerPingInterval)
            {
                _form.SetStatusMessage($"Checking controller on {portName}");
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
            switch (message.Kind)
            {
                case LapProtocolMessageKind.Edge:
                    if (message.Edge != null)
                    {
                        HandleEdge(message.Edge);
                    }
                    break;
                case LapProtocolMessageKind.Hello:
                    _form.SetStatusMessage(
                        message.Detail.Contains("RESETTING", StringComparison.OrdinalIgnoreCase)
                            ? message.Detail
                            : $"Controller responding on {_form.port}");
                    _log.Info(message.Detail);
                    break;
                case LapProtocolMessageKind.Heartbeat:
                    _form.SetStatusMessage($"Controller responding on {_form.port}");
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

        private void HandleEdge(LapEdge edge)
        {
            LapUpdate update = _race.Process(edge);
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
                if (update.Kind == LapUpdateKind.Invalid)
                {
                    _form.SetStatusMessage($"Lane {edge.LaneIndex + 1}: {update.Detail}");
                }
                return;
            }

            if (!update.LapMilliseconds.HasValue)
            {
                return;
            }

            int laneIndex = update.LaneIndex;
            int lapMilliseconds = update.LapMilliseconds.Value;
            Lane lane = _race.GetLane(laneIndex);
            string lapSeconds = FormatSeconds(lapMilliseconds);
            string bestSeconds = FormatSeconds(lane.best_time == int.MaxValue ? 0 : lane.best_time);
            string medianSeconds = FormatSeconds(lane.getMedian());
            string mph = _race.CalculateMilesPerHour(lapMilliseconds).ToString("F1", CultureInfo.InvariantCulture);

            _form.UpdateLaneDisplay(laneIndex, lane.getCount(), lapSeconds, bestSeconds, medianSeconds, mph);

            _log.Info($"lane {laneIndex}: lap {lapSeconds}s, count {lane.getCount()}, {update.Detail}");
            _form.SetStatusMessage($"Lane {laneIndex + 1}: lap {lapSeconds}s");
        }

        private static string FormatSeconds(int milliseconds) =>
            TimeSpan.FromMilliseconds(milliseconds).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

        private static void SavePort(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                return;
            }

            try
            {
                using SqliteCommand command = MKTS.conn.CreateCommand();
                command.CommandText = @"update comports set name=$name";
                command.Parameters.AddWithValue("$name", portName);
                command.ExecuteNonQuery();
            }
            catch
            {
                // Port persistence is helpful, but losing it should not stop timing.
            }
        }

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
            _stop.Cancel();
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
    }
}

