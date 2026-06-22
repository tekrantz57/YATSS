using System.Globalization;
using System.IO.Ports;
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
        private readonly Label[] _laps;
        private readonly Label[] _lastLap;
        private readonly Label[] _bestLap;
        private readonly Label[] _medianLap;
        private readonly Label[] _mph;
        private Task? _readerTask;
        private SerialPort? _port;

        public Serial(MKTS form)
        {
            _form = form;
            _laps = new[] { form.laps0, form.laps1, form.laps2, form.laps3, form.laps4, form.laps5, form.laps6, form.laps7 };
            _lastLap = new[] { form.ll0, form.ll1, form.ll2, form.ll3, form.ll4, form.ll5, form.ll6, form.ll7 };
            _bestLap = new[] { form.bl0, form.bl1, form.bl2, form.bl3, form.bl4, form.bl5, form.bl6, form.bl7 };
            _medianLap = new[] { form.ml0, form.ml1, form.ml2, form.ml3, form.ml4, form.ml5, form.ml6, form.ml7 };
            _mph = new[] { form.mph0, form.mph1, form.mph2, form.mph3, form.mph4, form.mph5, form.mph6, form.mph7 };

            Init();
            EnsurePortSelected();
            _readerTask = Task.Run(ReadLoopAsync);
        }

        public void Init()
        {
            _race.Reset();
            RunOnUiThread(() =>
            {
                for (int i = 0; i < LapProtocolParser.LaneCount; i++)
                {
                    _laps[i].Text = "0";
                    _lastLap[i].Text = "0.000";
                    _bestLap[i].Text = "0.000";
                    _medianLap[i].Text = "0.000";
                    _mph[i].Text = "0.0";
                }
            });
            _log.Info("race state reset");
        }

        public void ResetRace(bool resetArduino)
        {
            Init();
            if (resetArduino)
            {
                WriteLine("RESET");
            }
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
                CloseActivePort();
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

                    _log.Info($"serial connected on {portName}");

                    while (!_stop.IsCancellationRequested && port.IsOpen)
                    {
                        string line;
                        try
                        {
                            line = port.ReadLine();
                        }
                        catch (TimeoutException)
                        {
                            continue;
                        }

                        HandleLine(line);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                {
                    _log.Error(ex, $"serial disconnected from {portName}");
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
                case LapProtocolMessageKind.Error:
                    _log.Info(message.Detail);
                    break;
                case LapProtocolMessageKind.Ignored:
                    break;
                default:
                    _log.Warn($"rejected serial line '{message.RawLine}': {message.Detail}");
                    break;
            }
        }

        private void HandleEdge(LapEdge edge)
        {
            LapUpdate update = _race.Process(edge);
            if (update.Kind == LapUpdateKind.Started || update.Kind == LapUpdateKind.Duplicate || update.Kind == LapUpdateKind.Invalid)
            {
                _log.Info($"lane {edge.LaneIndex}: {update.Detail}");
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

            RunOnUiThread(() =>
            {
                _laps[laneIndex].Text = lane.getCount().ToString(CultureInfo.InvariantCulture);
                _lastLap[laneIndex].Text = lapSeconds;
                _bestLap[laneIndex].Text = bestSeconds;
                _medianLap[laneIndex].Text = medianSeconds;
                _mph[laneIndex].Text = mph;
            });

            _log.Info($"lane {laneIndex}: lap {lapSeconds}s, count {lane.getCount()}, {update.Detail}");
        }

        private static string FormatSeconds(int milliseconds) =>
            TimeSpan.FromMilliseconds(milliseconds).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

        private void EnsurePortSelected()
        {
            if (!string.IsNullOrWhiteSpace(_form.port))
            {
                return;
            }

            using SelectPort selectPort = new(_form.port);
            if (selectPort.ShowDialog(_form) == DialogResult.OK || !string.IsNullOrWhiteSpace(selectPort.port))
            {
                _form.port = selectPort.port;
                SavePort(_form.port);
            }
        }

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

        private void RunOnUiThread(Action action)
        {
            if (_form.IsDisposed)
            {
                return;
            }

            if (_form.InvokeRequired)
            {
                _form.BeginInvoke(action);
                return;
            }

            action();
        }

        private async Task DelayReconnectAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);
            }
            catch (OperationCanceledException)
            {
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

