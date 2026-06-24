using System.Diagnostics;
using System.Drawing.Text;
using System.IO.Ports;
using System.Xml.Serialization;
using Microsoft.Data.Sqlite;

namespace tlp
{
    public partial class MKTS : Form
    {
        static Serial s = null!;
        private Label[] _boardValueLabels = Array.Empty<Label>();
        private Label[] _boardHeaderLabels = Array.Empty<Label>();
        private Label[] _nameLabels = Array.Empty<Label>();
        private Label[] _lapLabels = Array.Empty<Label>();
        private Label[] _lastLapLabels = Array.Empty<Label>();
        private Label[] _bestLapLabels = Array.Empty<Label>();
        private Label[] _medianLapLabels = Array.Empty<Label>();
        private Label[] _mphLabels = Array.Empty<Label>();
        private const string EmptyRacerName = "          ";
        public string port = "";
        public int MinLapMilliseconds { get; private set; } = LapRaceOptions.Default.MinLapMilliseconds;
        public bool SoundOnTooFastLap { get; private set; } = true;
        public string SpeechVoiceName { get; private set; } = "";
        public static SqliteConnection conn = new SqliteConnection(@"Data Source=c:\sqlite\data\laps.db");

        public MKTS()
        {
            InitializeComponent();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
            KeyPreview = true;
            SpeechAnnouncer.WarmUpAsync(SpeechVoiceName);
            ConfigureBoardLayout();
            conn.Open();
            var command = conn.CreateCommand();
            command.CommandText = @"SELECT name FROM comports limit 1";

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    port = reader.GetString(0);
                }
            }

            s = new Serial(this);
            WireBestLapResetClicks();
            FormClosed += (_, _) => s.Dispose();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space)
            {
                s.HandleSpaceBar();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void fileToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Trace.WriteLine("practice reset");
            s.ResetRace(resetArduino: true);
        }

        private void practiceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetPracticeMode();
            s.SetPracticeMode();
        }

        private void heatRaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using HeatRaceSetup heatRaceSetup = new();
            if (heatRaceSetup.ShowDialog(this) == DialogResult.OK)
            {
                SetHeatRaceMode();
                s.ConfigureHeatRace(heatRaceSetup.HeatLengthMinutes, heatRaceSetup.BetweenHeatsSeconds);
            }
        }

        private void SetPracticeMode()
        {
            practiceToolStripMenuItem.Checked = true;
            heatRaceToolStripMenuItem.Checked = false;
        }

        private void SetHeatRaceMode()
        {
            practiceToolStripMenuItem.Checked = false;
            heatRaceToolStripMenuItem.Checked = true;
        }

        private void WireBestLapResetClicks()
        {
            for (int i = 0; i < _bestLapLabels.Length; i++)
            {
                _bestLapLabels[i].Tag = i;
                _bestLapLabels[i].Cursor = Cursors.Hand;
                _bestLapLabels[i].MouseClick += bestLapLabel_MouseClick;
            }
        }

        private void bestLapLabel_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || sender is not Label { Tag: int laneIndex })
            {
                return;
            }

            s.ResetLane(laneIndex);
        }

        private void ConfigureBoardLayout()
        {
            _boardHeaderLabels = new[] { label2, label4, label6, label8, label10, label12 };
            _nameLabels = new[] { name0, name1, name2, name3, name4, name5, name6, name7 };
            _lapLabels = new[] { laps0, laps1, laps2, laps3, laps4, laps5, laps6, laps7 };
            _lastLapLabels = new[] { ll0, ll1, ll2, ll3, ll4, ll5, ll6, ll7 };
            _bestLapLabels = new[] { bl0, bl1, bl2, bl3, bl4, bl5, bl6, bl7 };
            _medianLapLabels = new[] { ml0, ml1, ml2, ml3, ml4, ml5, ml6, ml7 };
            _mphLabels = new[] { mph0, mph1, mph2, mph3, mph4, mph5, mph6, mph7 };
            _boardValueLabels = new[]
            {
                name0, laps0, ll0, bl0, ml0, mph0,
                name1, laps1, ll1, bl1, ml1, mph1,
                name2, laps2, ll2, bl2, ml2, mph2,
                name3, laps3, ll3, bl3, ml3, mph3,
                name4, laps4, ll4, bl4, ml4, mph4,
                name5, laps5, ll5, bl5, ml5, mph5,
                name6, laps6, ll6, bl6, ml6, mph6,
                name7, laps7, ll7, bl7, ml7, mph7
            };

            foreach (Label label in _boardHeaderLabels.Concat(_boardValueLabels))
            {
                label.AutoSize = false;
                label.Dock = DockStyle.Fill;
                label.Margin = Padding.Empty;
                label.TextAlign = ContentAlignment.MiddleCenter;
            }

            labelMKTS.AutoSize = false;
            labelMKTS.TextAlign = ContentAlignment.MiddleCenter;
            ApplyBoardFonts();
        }

        private void MKTS_Load(object sender, EventArgs e)
        {
            ApplyBoardFonts();
        }

        private void MKTS_Resize(object sender, EventArgs e)
        {
            ApplyBoardFonts();
        }

        private void ApplyBoardFonts()
        {
            foreach (Label label in _boardHeaderLabels)
            {
                SetFontSize(label, label.Height * 0.45f);
            }

            foreach (Label label in _boardValueLabels)
            {
                SetFontSize(label, label.Height * 0.48f);
            }

            SetFontSizeToFit(labelMKTS, labelMKTS.Height * 0.4f);
        }

        public void ResetBoardDisplay(bool clearRacers)
        {
            RunOnUiThread(() =>
            {
                for (int i = 0; i < LapProtocolParser.LaneCount; i++)
                {
                    ResetLaneDisplayCore(i, clearRacers);
                }
            });
        }

        public void ResetLaneDisplay(int laneIndex, bool clearRacer)
        {
            if (laneIndex < 0 || laneIndex >= LapProtocolParser.LaneCount)
            {
                return;
            }

            RunOnUiThread(() => ResetLaneDisplayCore(laneIndex, clearRacer));
        }

        public void UpdateLaneDisplay(
            int laneIndex,
            int lapCount,
            string lastLap,
            string bestLap,
            string medianLap,
            string milesPerHour)
        {
            if (laneIndex < 0 || laneIndex >= LapProtocolParser.LaneCount)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                _lapLabels[laneIndex].Text = lapCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _lastLapLabels[laneIndex].Text = lastLap;
                _bestLapLabels[laneIndex].Text = bestLap;
                _medianLapLabels[laneIndex].Text = medianLap;
                _mphLabels[laneIndex].Text = milesPerHour;
            });
        }

        public void SetStatusMessage(string message)
        {
            RunOnUiThread(() => statusMessageLabel.Text = message);
        }

        private void ResetLaneDisplayCore(int laneIndex, bool clearRacer)
        {
            if (clearRacer)
            {
                _nameLabels[laneIndex].Text = EmptyRacerName;
            }

            _lapLabels[laneIndex].Text = "0";
            _lastLapLabels[laneIndex].Text = "0.000";
            _bestLapLabels[laneIndex].Text = "0.000";
            _medianLapLabels[laneIndex].Text = "0.000";
            _mphLabels[laneIndex].Text = "0.0";
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }

            action();
        }

        private static void SetFontSize(Label label, float requestedSize)
        {
            if (label.Height <= 0)
            {
                return;
            }

            float size = Math.Clamp(requestedSize, 10f, 72f);
            if (Math.Abs(label.Font.Size - size) < 0.5f)
            {
                return;
            }

            label.Font = new Font(label.Font.FontFamily, size, label.Font.Style);
        }

        private static void SetFontSizeToFit(Label label, float requestedSize)
        {
            if (label.Height <= 0 || label.Width <= 0 || string.IsNullOrWhiteSpace(label.Text))
            {
                return;
            }

            const float minimumSize = 10f;
            float size = Math.Clamp(requestedSize, minimumSize, 72f);
            Size available = new(Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal - 8),
                Math.Max(1, label.ClientSize.Height - label.Padding.Vertical - 4));

            using Graphics graphics = label.CreateGraphics();
            while (size > minimumSize)
            {
                using Font testFont = new(label.Font.FontFamily, size, label.Font.Style);
                SizeF measured = graphics.MeasureString(label.Text, testFont);
                if (measured.Width <= available.Width && measured.Height <= available.Height)
                {
                    break;
                }

                size -= 1f;
            }

            SetFontSize(label, size);
        }

        private void editUsersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var editUsers = new EditUsers())
            {
                editUsers.ShowDialog();
            }
        }

        private void nameLabel_Click(object sender, EventArgs e)
        {
            if (sender is Label nameLabel)
            {
                ShowRacerMenu(nameLabel);
            }
        }

        private void ShowRacerMenu(Label nameLabel)
        {
            contextMenuStrip1 = new ContextMenuStrip
            {
                Tag = nameLabel
            };

            foreach (string racerName in LoadRacerNames())
            {
                contextMenuStrip1.Items.Add(racerName);
            }

            contextMenuStrip1.ItemClicked += racerNameMenu_ItemClicked;
            contextMenuStrip1.Show(Cursor.Position);
        }

        private static List<string> LoadRacerNames()
        {
            List<string> racerNames = new();
            var command = conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    racerNames.Add(reader.GetString(0));
                }
            }

            return racerNames;
        }

        private void racerNameMenu_ItemClicked(object? sender, ToolStripItemClickedEventArgs e)
        {
            if (sender is ContextMenuStrip { Tag: Label nameLabel })
            {
                nameLabel.Text = e.ClickedItem?.Text ?? string.Empty;
            }
        }

        private void configureToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using Configure config = new Configure(MinLapMilliseconds, SoundOnTooFastLap, port, SpeechVoiceName);
            if (config.ShowDialog(this) == DialogResult.OK)
            {
                MinLapMilliseconds = config.MinLapMilliseconds;
                SoundOnTooFastLap = config.SoundOnTooFastLap;
                SpeechVoiceName = config.SelectedSpeechVoice;
                SpeechAnnouncer.WarmUpAsync(SpeechVoiceName);
                s.ApplySettings();
                s.SetPort(config.SelectedPort);
            }
        }

        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }

        private void labelMKTS_Click(object sender, EventArgs e)
        {

        }
    }
}
