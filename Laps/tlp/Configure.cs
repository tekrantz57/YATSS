using System.IO.Ports;

namespace tlp
{
    public partial class Configure : Form
    {
        public int MinLapMilliseconds { get; private set; }
        public bool SoundOnTooFastLap { get; private set; }
        public string SelectedPort { get; private set; } = "";
        public string SelectedSpeechVoice { get; private set; } = "";
        public int ActiveLaneCount { get; private set; }
        public IReadOnlyList<LaneConfiguration> LaneConfigurations { get; private set; }
        private readonly TextBox[] _laneNameTextBoxes = new TextBox[LapProtocolParser.LaneCount];
        private readonly Button[] _laneColorButtons = new Button[LapProtocolParser.LaneCount];
        private readonly ToolTip _laneColorToolTip = new();

        public Configure(
            int minLapMilliseconds,
            bool soundOnTooFastLap,
            string selectedPort,
            string selectedSpeechVoice,
            int activeLaneCount,
            IReadOnlyList<LaneConfiguration> laneConfigurations)
        {
            InitializeComponent();
            MinLapMilliseconds = minLapMilliseconds;
            SoundOnTooFastLap = soundOnTooFastLap;
            SelectedPort = selectedPort;
            SelectedSpeechVoice = selectedSpeechVoice;
            ActiveLaneCount = activeLaneCount;
            LaneConfigurations = NormalizeLaneConfigurations(laneConfigurations);
            nudMinLapMilliseconds.Value = Math.Clamp(minLapMilliseconds, (int)nudMinLapMilliseconds.Minimum, (int)nudMinLapMilliseconds.Maximum);
            cbSoundOnTooFastLap.Checked = soundOnTooFastLap;
            nudActiveLaneCount.Value = Math.Clamp(activeLaneCount, (int)nudActiveLaneCount.Minimum, (int)nudActiveLaneCount.Maximum);
            BuildLaneColorEditor();
            LoadSerialPorts(selectedPort);
            LoadSpeechVoices(selectedSpeechVoice);
        }

        private void LoadSerialPorts(string selectedPort)
        {
            cbSerialPort.Items.Clear();
            foreach (string portName in SerialPort.GetPortNames().OrderBy(p => p))
            {
                cbSerialPort.Items.Add(portName);
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
            foreach (string voiceName in SpeechAnnouncer.GetInstalledVoices())
            {
                cbSpeechVoice.Items.Add(voiceName);
            }

            cbSpeechVoice.Text = selectedSpeechVoice;
        }

        private void bOK_Click(object sender, EventArgs e)
        {
            MinLapMilliseconds = (int)nudMinLapMilliseconds.Value;
            SoundOnTooFastLap = cbSoundOnTooFastLap.Checked;
            SelectedPort = cbSerialPort.Text.Trim();
            SelectedSpeechVoice = cbSpeechVoice.Text.Trim();
            ActiveLaneCount = (int)nudActiveLaneCount.Value;
            LaneConfigurations = Enumerable.Range(0, LapProtocolParser.LaneCount)
                .Select(lane => new LaneConfiguration(
                    GetLaneName(lane),
                    _laneColorButtons[lane].BackColor.ToArgb()))
                .ToArray();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BuildLaneColorEditor()
        {
            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(6)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            for (int row = 0; row < 4; row++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            }

            groupBoxLaneColors.Controls.Add(layout);
            for (int lane = 0; lane < LapProtocolParser.LaneCount; lane++)
            {
                FlowLayoutPanel lanePanel = new()
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    Margin = Padding.Empty
                };

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

                layout.Controls.Add(lanePanel, lane % 2, lane / 2);
            }
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
