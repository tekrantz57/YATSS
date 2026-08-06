using System.IO.Ports;

namespace YATSS
{
    public partial class Configure : Form
    {
        public int MinLapMilliseconds { get; private set; }
        public bool SoundOnTooFastLap { get; private set; }
        public bool VoiceAnnouncementsEnabled { get; private set; }
        public string SelectedPort { get; private set; } = "";
        public string SelectedSpeechVoice { get; private set; } = "";
        public SpeechBackendMode SelectedSpeechBackend { get; private set; }
        public int ActiveLaneCount { get; private set; }
        public double TrackLengthFeet { get; private set; }
        public int SensorDebounceMilliseconds { get; private set; }
        public int RawSensorLockoutMilliseconds { get; private set; }
        public bool ExportRaceJson { get; private set; }
        public bool ExportRaceCsv { get; private set; }
        public IReadOnlyList<LaneConfiguration> LaneConfigurations { get; private set; }
        private readonly TextBox[] _laneNameTextBoxes = new TextBox[LapProtocolParser.LaneCount];
        private readonly Button[] _laneColorButtons = new Button[LapProtocolParser.LaneCount];
        private readonly FlowLayoutPanel[] _laneEditorPanels = new FlowLayoutPanel[LapProtocolParser.LaneCount];
        private readonly ToolTip _laneColorToolTip = new();
        private TableLayoutPanel _laneColorLayout = null!;

        public Configure(
            int minLapMilliseconds,
            bool soundOnTooFastLap,
            string selectedPort,
            bool voiceAnnouncementsEnabled,
            string selectedSpeechVoice,
            SpeechBackendMode selectedSpeechBackend,
            int activeLaneCount,
            double trackLengthFeet,
            int sensorDebounceMilliseconds,
            int rawSensorLockoutMilliseconds,
            bool exportRaceJson,
            bool exportRaceCsv,
            IReadOnlyList<LaneConfiguration> laneConfigurations)
        {
            InitializeComponent();
            MinLapMilliseconds = minLapMilliseconds;
            SoundOnTooFastLap = soundOnTooFastLap;
            VoiceAnnouncementsEnabled = voiceAnnouncementsEnabled;
            SelectedPort = selectedPort;
            SelectedSpeechVoice = selectedSpeechVoice;
            SelectedSpeechBackend = selectedSpeechBackend;
            ActiveLaneCount = activeLaneCount;
            TrackLengthFeet = trackLengthFeet;
            SensorDebounceMilliseconds = sensorDebounceMilliseconds;
            RawSensorLockoutMilliseconds = rawSensorLockoutMilliseconds;
            ExportRaceJson = exportRaceJson;
            ExportRaceCsv = exportRaceCsv;
            LaneConfigurations = NormalizeLaneConfigurations(laneConfigurations);
            nudMinLapMilliseconds.Value = Math.Clamp(minLapMilliseconds, (int)nudMinLapMilliseconds.Minimum, (int)nudMinLapMilliseconds.Maximum);
            nudSensorDebounceMilliseconds.Value = Math.Clamp(sensorDebounceMilliseconds, (int)nudSensorDebounceMilliseconds.Minimum, (int)nudSensorDebounceMilliseconds.Maximum);
            nudRawSensorLockoutMilliseconds.Value = Math.Clamp(rawSensorLockoutMilliseconds, (int)nudRawSensorLockoutMilliseconds.Minimum, (int)nudRawSensorLockoutMilliseconds.Maximum);
            cbSoundOnTooFastLap.Checked = soundOnTooFastLap;
            cbVoiceAnnouncements.Checked = voiceAnnouncementsEnabled;
            cbExportRaceJson.Checked = exportRaceJson;
            cbExportRaceCsv.Checked = exportRaceCsv;
            nudActiveLaneCount.Value = Math.Clamp(activeLaneCount, (int)nudActiveLaneCount.Minimum, (int)nudActiveLaneCount.Maximum);
            nudTrackLengthFeet.Value = Math.Clamp(
                (decimal)trackLengthFeet,
                nudTrackLengthFeet.Minimum,
                nudTrackLengthFeet.Maximum);
            BuildLaneColorEditor();
            nudActiveLaneCount.ValueChanged += (_, _) => ApplyActiveLaneEditors();
            LoadSerialPorts(selectedPort);
            InitializeSpeechBackends(selectedSpeechBackend);
            InitializeSpeechVoices(selectedSpeechVoice);
            cbVoiceAnnouncements.CheckedChanged += (_, _) => ApplyVoiceAnnouncementState();
            cbSpeechBackend.SelectedIndexChanged += (_, _) =>
            {
                LoadSpeechVoices(cbSpeechVoice.Text);
                cbSpeechVoice.Enabled = cbVoiceAnnouncements.Checked &&
                    GetSelectedSpeechBackend() != SpeechBackendMode.None;
            };
        }

        private void InitializeSpeechBackends(SpeechBackendMode selectedBackend)
        {
            cbSpeechBackend.Items.Clear();
            cbSpeechBackend.Items.AddRange(new object[]
            {
                new SpeechBackendOption(SpeechBackendMode.Automatic, "Automatic"),
                new SpeechBackendOption(SpeechBackendMode.WindowsSapi, "Windows SAPI"),
                new SpeechBackendOption(SpeechBackendMode.LinuxHelper, "Linux helper"),
                new SpeechBackendOption(SpeechBackendMode.None, "None")
            });
            cbSpeechBackend.SelectedItem = cbSpeechBackend.Items
                .Cast<SpeechBackendOption>()
                .First(option => option.Mode == selectedBackend);
        }

        private void InitializeSpeechVoices(string selectedSpeechVoice)
        {
            cbSpeechVoice.Items.Clear();
            if (!string.IsNullOrWhiteSpace(selectedSpeechVoice))
            {
                cbSpeechVoice.Items.Add(selectedSpeechVoice);
            }

            cbSpeechVoice.Text = selectedSpeechVoice;
            ApplyVoiceAnnouncementState();
        }

        private void ApplyVoiceAnnouncementState()
        {
            cbSpeechBackend.Enabled = cbVoiceAnnouncements.Checked;
            bool voiceSelectionEnabled = cbVoiceAnnouncements.Checked &&
                GetSelectedSpeechBackend() != SpeechBackendMode.None;
            cbSpeechVoice.Enabled = voiceSelectionEnabled;
            if (voiceSelectionEnabled && cbSpeechVoice.Items.Count <= 1)
            {
                LoadSpeechVoices(cbSpeechVoice.Text);
            }
        }

        private void LoadSerialPorts(string selectedPort)
        {
            cbSerialPort.Items.Clear();
            foreach (string portName in SerialPort.GetPortNames().OrderBy(p => p))
            {
                cbSerialPort.Items.Add(portName);
            }

            if (PlatformEnvironment.IsWine)
            {
                cbSerialPort.Items.Add(ControllerEndpoint.UnoQ);
            }

            if (!string.IsNullOrWhiteSpace(selectedPort) && !cbSerialPort.Items.Contains(selectedPort))
            {
                cbSerialPort.Items.Add(selectedPort);
            }

            cbSerialPort.Text = selectedPort;
        }

        private void LoadSpeechVoices(string selectedSpeechVoice)
        {
            cbSpeechVoice.Items.Clear();
            cbSpeechVoice.Items.Add("");
            foreach (string voiceName in SpeechAnnouncer.GetInstalledVoices(GetSelectedSpeechBackend()))
            {
                cbSpeechVoice.Items.Add(voiceName);
            }

            cbSpeechVoice.Text = selectedSpeechVoice;
        }

        private void bOK_Click(object sender, EventArgs e)
        {
            MinLapMilliseconds = (int)nudMinLapMilliseconds.Value;
            SoundOnTooFastLap = cbSoundOnTooFastLap.Checked;
            VoiceAnnouncementsEnabled = cbVoiceAnnouncements.Checked;
            SelectedPort = cbSerialPort.Text.Trim();
            SelectedSpeechVoice = cbSpeechVoice.Text.Trim();
            SelectedSpeechBackend = GetSelectedSpeechBackend();
            ActiveLaneCount = (int)nudActiveLaneCount.Value;
            TrackLengthFeet = (double)nudTrackLengthFeet.Value;
            SensorDebounceMilliseconds = (int)nudSensorDebounceMilliseconds.Value;
            RawSensorLockoutMilliseconds = (int)nudRawSensorLockoutMilliseconds.Value;
            ExportRaceJson = cbExportRaceJson.Checked;
            ExportRaceCsv = cbExportRaceCsv.Checked;
            LaneConfigurations = Enumerable.Range(0, LapProtocolParser.LaneCount)
                .Select(lane => new LaneConfiguration(
                    GetLaneName(lane),
                    _laneColorButtons[lane].BackColor.ToArgb()))
                .ToArray();
            DialogResult = DialogResult.OK;
            Close();
        }

        private SpeechBackendMode GetSelectedSpeechBackend() =>
            cbSpeechBackend.SelectedItem is SpeechBackendOption option
                ? option.Mode
                : SpeechBackendMode.Automatic;

        private sealed record SpeechBackendOption(SpeechBackendMode Mode, string DisplayName)
        {
            public override string ToString() => DisplayName;
        }

        private void BuildLaneColorEditor()
        {
            _laneColorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(6)
            };
            _laneColorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _laneColorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            for (int row = 0; row < 4; row++)
            {
                _laneColorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            }

            groupBoxLaneColors.Controls.Add(_laneColorLayout);
            for (int lane = 0; lane < LapProtocolParser.LaneCount; lane++)
            {
                FlowLayoutPanel lanePanel = new()
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    Margin = Padding.Empty
                };
                _laneEditorPanels[lane] = lanePanel;

                Label laneLabel = new()
                {
                    Text = $"{lane + 1}",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleRight,
                    Width = 18,
                    Height = 27
                };
                lanePanel.Controls.Add(laneLabel);

                TextBox nameTextBox = new()
                {
                    Text = LaneConfigurations[lane].Name,
                    Width = 92,
                    MaxLength = 20
                };
                _laneNameTextBoxes[lane] = nameTextBox;
                lanePanel.Controls.Add(nameTextBox);

                Button colorButton = new()
                {
                    BackColor = LaneConfigurations[lane].Color,
                    UseVisualStyleBackColor = false,
                    Width = 42,
                    Height = 25,
                    Tag = lane
                };
                colorButton.Click += laneColorButton_Click;
                _laneColorButtons[lane] = colorButton;
                _laneColorToolTip.SetToolTip(colorButton, $"Choose color for lane {lane + 1}");
                lanePanel.Controls.Add(colorButton);

                _laneColorLayout.Controls.Add(lanePanel, lane % 2, lane / 2);
            }

            ApplyActiveLaneEditors();
        }

        private void ApplyActiveLaneEditors()
        {
            int activeLaneCount = (int)nudActiveLaneCount.Value;
            int visibleRows = (activeLaneCount + 1) / 2;
            for (int lane = 0; lane < _laneEditorPanels.Length; lane++)
            {
                _laneEditorPanels[lane].Visible = lane < activeLaneCount;
            }

            for (int row = 0; row < _laneColorLayout.RowStyles.Count; row++)
            {
                RowStyle rowStyle = _laneColorLayout.RowStyles[row];
                rowStyle.SizeType = row < visibleRows ? SizeType.Percent : SizeType.Absolute;
                rowStyle.Height = row < visibleRows ? 100F / visibleRows : 0F;
            }

            int groupHeight = 30 + (visibleRows * 36);
            groupBoxLaneColors.Height = groupHeight;
            int buttonTop = groupBoxLaneColors.Bottom + 18;
            bOK.Top = buttonTop;
            bCancel.Top = buttonTop;
            ClientSize = new Size(ClientSize.Width, buttonTop + bOK.Height + 16);
            groupBoxLaneColors.PerformLayout();
        }

        private void laneColorButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: int lane })
            {
                return;
            }

            using ColorDialog dialog = new()
            {
                Color = _laneColorButtons[lane].BackColor,
                FullOpen = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _laneColorButtons[lane].BackColor = dialog.Color;
            }
        }

        private string GetLaneName(int lane)
        {
            string name = _laneNameTextBoxes[lane].Text.Trim();
            return string.IsNullOrWhiteSpace(name) ? $"Lane {lane + 1}" : name;
        }

        private static IReadOnlyList<LaneConfiguration> NormalizeLaneConfigurations(
            IReadOnlyList<LaneConfiguration> laneConfigurations)
        {
            LaneConfiguration[] defaults = LaneConfiguration.CreateDefaults().ToArray();
            for (int lane = 0; lane < defaults.Length && lane < laneConfigurations.Count; lane++)
            {
                defaults[lane] = laneConfigurations[lane];
            }

            return defaults;
        }

        private void bCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
