using System.Globalization;

namespace YATSS
{
    public sealed class ControllerDiagnosticsForm : Form
    {
        private readonly Label[] _sensorLabels = new Label[LapProtocolParser.LaneCount];
        private readonly Label[] _transitionLabels = new Label[LapProtocolParser.LaneCount];
        private readonly Label[] _acceptedEdgeLabels = new Label[LapProtocolParser.LaneCount];
        private readonly Label[] _relayLabels = new Label[LapProtocolParser.LaneCount];
        private readonly Button[] _pulseButtons = new Button[LapProtocolParser.LaneCount];
        private readonly Label _connectionLabel = new();
        private readonly Label _healthLabel = new();
        private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 1000 };
        private readonly Action _requestStatus;
        private DateTime _lastResponseUtc;
        private bool _sessionStarted;

        public ControllerDiagnosticsForm(
            string portName,
            IReadOnlyList<LaneConfiguration> laneConfigurations,
            Action requestStatus,
            Action clearCounts,
            Action<int> pulseRelay,
            Action cutAllPower)
        {
            _requestStatus = requestStatus;
            Text = "Controller Diagnostics";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(880, 510);
            ClientSize = new Size(940, 540);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(root);

            TableLayoutPanel summary = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            summary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            _connectionLabel.AutoSize = true;
            _connectionLabel.Font = new Font(Font, FontStyle.Bold);
            _connectionLabel.Text = $"Connecting to controller on {portName}";
            _healthLabel.AutoSize = true;
            _healthLabel.Text = "Waiting for diagnostic status";
            summary.Controls.Add(_connectionLabel, 0, 0);
            summary.Controls.Add(_healthLabel, 0, 1);
            root.Controls.Add(summary, 0, 0);

            TableLayoutPanel lanes = BuildLaneTable(laneConfigurations, pulseRelay);
            root.Controls.Add(lanes, 0, 1);

            FlowLayoutPanel commands = new()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };
            Button refreshButton = CreateCommandButton("Refresh", (_, _) => requestStatus());
            Button clearButton = CreateCommandButton("Clear Counts", (_, _) =>
            {
                foreach (Label label in _transitionLabels)
                {
                    label.Text = "0";
                }
                foreach (Label label in _acceptedEdgeLabels)
                {
                    label.Text = "0";
                }
                clearCounts();
            });
            Button cutAllButton = CreateCommandButton("Cut All Power", (_, _) => cutAllPower());
            cutAllButton.BackColor = Color.Firebrick;
            cutAllButton.ForeColor = Color.White;
            Button closeButton = CreateCommandButton("Close", (_, _) => Close());
            commands.Controls.Add(refreshButton);
            commands.Controls.Add(clearButton);
            commands.Controls.Add(cutAllButton);
            commands.Controls.Add(closeButton);
            root.Controls.Add(commands, 0, 2);

            _statusTimer.Tick += (_, _) =>
            {
                _requestStatus();
                if (_lastResponseUtc != default && DateTime.UtcNow - _lastResponseUtc > TimeSpan.FromSeconds(3))
                {
                    _connectionLabel.Text = $"Controller response is stale on {portName}";
                    _connectionLabel.ForeColor = Color.Firebrick;
                }
            };
            Shown += (_, _) =>
            {
                _statusTimer.Start();
                _requestStatus();
            };
            FormClosed += (_, _) =>
            {
                _statusTimer.Stop();
                _statusTimer.Dispose();
            };
        }

        public void ApplyDiagnostic(ControllerDiagnostic diagnostic)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(() => ApplyDiagnostic(diagnostic));
                }
                return;
            }

            _lastResponseUtc = DateTime.UtcNow;
            _connectionLabel.ForeColor = SystemColors.ControlText;

            switch (diagnostic)
            {
                case ControllerDiagnosticStatus status:
                    _connectionLabel.Text = "Controller diagnostics connected";
                    _healthLabel.Text =
                        $"Uptime {FormatUptime(status.TimestampMillis)}    " +
                        $"Debounce {status.DebounceMilliseconds} ms    Dropped events {status.DroppedEvents}";
                    for (int lane = 0; lane < LapProtocolParser.LaneCount; lane++)
                    {
                        SetSensorState(lane, (status.SensorActiveMask & (1 << lane)) != 0);
                        SetRelayState(lane, (status.TrackPowerEnabledMask & (1 << lane)) != 0);
                    }
                    break;

                case ControllerDiagnosticSensor sensor:
                    SetSensorState(sensor.LaneIndex, sensor.Active);
                    _transitionLabels[sensor.LaneIndex].Text =
                        sensor.TransitionCount.ToString(CultureInfo.InvariantCulture);
                    _acceptedEdgeLabels[sensor.LaneIndex].Text =
                        sensor.AcceptedEdgeCount.ToString(CultureInfo.InvariantCulture);
                    break;

                case ControllerDiagnosticRelay relay:
                    SetRelayMask(relay.TrackPowerEnabledMask);
                    bool pulsing = string.Equals(relay.State, "PULSING", StringComparison.OrdinalIgnoreCase);
                    _relayLabels[relay.LaneIndex].Text = pulsing ? "Pulsing cut" : GetPowerText(relay.TrackPowerEnabledMask, relay.LaneIndex);
                    SetPulseButtonsEnabled(!pulsing);
                    break;

                case ControllerDiagnosticSession session:
                    _sessionStarted = string.Equals(session.State, "STARTED", StringComparison.OrdinalIgnoreCase);
                    _connectionLabel.Text = _sessionStarted
                        ? "Controller diagnostics connected"
                        : $"Diagnostic session stopped{FormatReason(session.Reason)}";
                    SetPulseButtonsEnabled(_sessionStarted);
                    break;
            }
        }

        private TableLayoutPanel BuildLaneTable(
            IReadOnlyList<LaneConfiguration> laneConfigurations,
            Action<int> pulseRelay)
        {
            TableLayoutPanel table = new()
            {
                Dock = DockStyle.Fill,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                ColumnCount = 6,
                RowCount = LapProtocolParser.LaneCount + 1,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            string[] headers = { "Lane", "Sensor", "Transitions", "Accepted", "Track power", "Relay test" };
            for (int column = 0; column < headers.Length; column++)
            {
                table.Controls.Add(CreateCellLabel(headers[column], bold: true), column, 0);
            }

            for (int lane = 0; lane < LapProtocolParser.LaneCount; lane++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 12.5F));
                LaneConfiguration configuration = lane < laneConfigurations.Count
                    ? laneConfigurations[lane]
                    : LaneConfiguration.CreateDefaults()[lane];
                Panel laneCell = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
                Panel swatch = new()
                {
                    BackColor = configuration.Color,
                    Size = new Size(18, 18),
                    Location = new Point(8, 8),
                    BorderStyle = BorderStyle.FixedSingle
                };
                Label laneLabel = new()
                {
                    AutoSize = true,
                    Location = new Point(34, 9),
                    Text = $"{lane + 1}  {configuration.Name}"
                };
                laneCell.Controls.Add(swatch);
                laneCell.Controls.Add(laneLabel);

                _sensorLabels[lane] = CreateCellLabel("Unknown");
                _transitionLabels[lane] = CreateCellLabel("0");
                _acceptedEdgeLabels[lane] = CreateCellLabel("0");
                _relayLabels[lane] = CreateCellLabel("Unknown");
                int capturedLane = lane;
                _pulseButtons[lane] = new Button
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(5),
                    Text = "Pulse Cut",
                    Enabled = false
                };
                _pulseButtons[lane].Click += (_, _) =>
                {
                    SetPulseButtonsEnabled(false);
                    pulseRelay(capturedLane);
                };

                int row = lane + 1;
                table.Controls.Add(laneCell, 0, row);
                table.Controls.Add(_sensorLabels[lane], 1, row);
                table.Controls.Add(_transitionLabels[lane], 2, row);
                table.Controls.Add(_acceptedEdgeLabels[lane], 3, row);
                table.Controls.Add(_relayLabels[lane], 4, row);
                table.Controls.Add(_pulseButtons[lane], 5, row);
            }

            return table;
        }

        private static Label CreateCellLabel(string text, bool bold = false)
        {
            Font baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
            return new Label
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 4, 0),
                Font = bold ? new Font(baseFont, FontStyle.Bold) : baseFont
            };
        }

        private static Button CreateCommandButton(string text, EventHandler onClick)
        {
            Button button = new()
            {
                AutoSize = true,
                MinimumSize = new Size(110, 30),
                Text = text
            };
            button.Click += onClick;
            return button;
        }

        private void SetSensorState(int laneIndex, bool active)
        {
            if (laneIndex < 0 || laneIndex >= _sensorLabels.Length)
            {
                return;
            }

            Label label = _sensorLabels[laneIndex];
            label.Text = active ? "ACTIVE" : "Clear";
            label.BackColor = active ? Color.Firebrick : Color.Honeydew;
            label.ForeColor = active ? Color.White : Color.DarkGreen;
        }

        private void SetRelayMask(byte enabledMask)
        {
            for (int lane = 0; lane < LapProtocolParser.LaneCount; lane++)
            {
                SetRelayState(lane, (enabledMask & (1 << lane)) != 0);
            }
        }

        private void SetRelayState(int laneIndex, bool powerEnabled)
        {
            Label label = _relayLabels[laneIndex];
            label.Text = powerEnabled ? "Power ON" : "Power CUT";
            label.ForeColor = powerEnabled ? SystemColors.ControlText : Color.Firebrick;
        }

        private static string GetPowerText(byte enabledMask, int laneIndex) =>
            (enabledMask & (1 << laneIndex)) != 0 ? "Power ON" : "Power CUT";

        private void SetPulseButtonsEnabled(bool enabled)
        {
            foreach (Button button in _pulseButtons)
            {
                button.Enabled = enabled && _sessionStarted;
            }
        }

        private static string FormatUptime(uint timestampMillis) =>
            TimeSpan.FromMilliseconds(timestampMillis).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture);

        private static string FormatReason(string reason) =>
            string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason.ToLowerInvariant()})";
    }
}
