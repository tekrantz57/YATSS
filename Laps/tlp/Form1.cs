using System.Diagnostics;

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
        private Label _heatStatusLabel = null!;
        private Label _heatTimerLabel = null!;
        private Label _onDeckLabel = null!;
        private const string EmptyRacerName = "          ";
        private const string DefaultWindowTitle = "MKTS";
        public string port = "";
        public int MinLapMilliseconds { get; private set; } = LapRaceOptions.Default.MinLapMilliseconds;
        public bool SoundOnTooFastLap { get; private set; } = true;
        public string SpeechVoiceName { get; private set; } = "";
        public int ActiveLaneCount { get; private set; } = LapProtocolParser.LaneCount;
        public double TrackLengthFeet { get; private set; } = LapRaceOptions.Default.TrackLengthFeet;
        public int SensorDebounceMilliseconds { get; private set; } = AppDatabase.DefaultSensorDebounceMilliseconds;
        public int RawSensorLockoutMilliseconds { get; private set; } = AppDatabase.DefaultRawSensorLockoutMilliseconds;
        public IReadOnlyList<LaneConfiguration> LaneConfigurations { get; private set; } =
            LaneConfiguration.CreateDefaults();

        public MKTS()
        {
            InitializeComponent();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
            KeyPreview = true;
            ConfigureBoardLayout();
            AppDatabase.Open();
            port = AppDatabase.LoadSerialPort();
            AppSettings settings = AppDatabase.LoadAppSettings(new AppSettings(
                MinLapMilliseconds,
                SoundOnTooFastLap,
                SpeechVoiceName,
                ActiveLaneCount));
            MinLapMilliseconds = Math.Clamp(settings.MinLapMilliseconds, 100, 60000);
            SoundOnTooFastLap = settings.SoundOnTooFastLap;
            SpeechVoiceName = settings.SpeechVoiceName;
            ActiveLaneCount = Math.Clamp(settings.ActiveLaneCount, 2, LapProtocolParser.LaneCount);
            TrackLengthFeet = Math.Clamp(
                AppDatabase.LoadTrackLengthFeet(TrackLengthFeet),
                1.0,
                10000.0);
            SensorDebounceMilliseconds = Math.Clamp(
                AppDatabase.LoadSensorDebounceMilliseconds(SensorDebounceMilliseconds),
                0,
                10000);
            RawSensorLockoutMilliseconds = Math.Clamp(
                AppDatabase.LoadRawSensorLockoutMilliseconds(RawSensorLockoutMilliseconds),
                0,
                10000);
            LaneConfigurations = AppDatabase.LoadLaneConfigurations(LaneConfigurations);
            ApplyLaneColors();
            ApplyActiveLaneLayout();
            SpeechAnnouncer.WarmUpAsync(SpeechVoiceName);

            s = new Serial(this);
            WireBestLapResetClicks();
            FormClosed += (_, _) => s.Dispose();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (TryHandleLapAdjustmentKey(keyData))
            {
                return true;
            }

            if (keyData == Keys.Space)
            {
                s.HandleSpaceBar();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static bool TryGetLaneKey(Keys keyCode, out int laneIndex)
        {
            laneIndex = keyCode switch
            {
                Keys.D1 or Keys.NumPad1 => 0,
                Keys.D2 or Keys.NumPad2 => 1,
                Keys.D3 or Keys.NumPad3 => 2,
                Keys.D4 or Keys.NumPad4 => 3,
                Keys.D5 or Keys.NumPad5 => 4,
                Keys.D6 or Keys.NumPad6 => 5,
                Keys.D7 or Keys.NumPad7 => 6,
                Keys.D8 or Keys.NumPad8 => 7,
                _ => -1
            };

            return laneIndex >= 0;
        }

        private bool TryHandleLapAdjustmentKey(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            if (!TryGetLaneKey(keyCode, out int laneIndex) || !keyData.HasFlag(Keys.Control))
            {
                return false;
            }

            int delta = keyData.HasFlag(Keys.Shift) ? -1 : 1;
            s.AdjustStoppedHeatLap(laneIndex, delta);
            return true;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Trace.WriteLine("practice reset");
            SetRaceTitle(null);
            SetQualifyingAvailable(false);
            s.ResetRace(resetArduino: true);
        }

        private void serialLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SerialLogTailForm logTail = new();
            logTail.Show(this);
        }

        private void practiceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ConfirmAbandonQualifying())
            {
                return;
            }

            SetPracticeMode();
            s.SetPracticeMode();
        }

        private void heatRaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ConfirmAbandonQualifying())
            {
                return;
            }

            s.CancelQualifying();
            using HeatRaceSetup heatRaceSetup = new(ActiveLaneCount, LaneConfigurations);
            if (heatRaceSetup.ShowDialog(this) == DialogResult.OK)
            {
                SetHeatRaceMode();
                s.ConfigureHeatRace(
                    heatRaceSetup.RaceName,
                    heatRaceSetup.HeatLengthMinutes,
                    heatRaceSetup.BetweenHeatsSeconds,
                    heatRaceSetup.SelectedRacers,
                    ActiveLaneCount,
                    LaneConfigurations,
                    TrackLengthFeet);
                SetRaceTitle(heatRaceSetup.RaceName);
                SetLaneRacerNames(heatRaceSetup.FirstHeatLaneRacers);
                SetQualifyingAvailable(true);
            }
        }

        private void qualifyingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using QualifyingSetup qualifyingSetup = new(ActiveLaneCount, LaneConfigurations);
            if (qualifyingSetup.ShowDialog(this) == DialogResult.OK)
            {
                s.ConfigureQualifying(
                    qualifyingSetup.LaneIndex,
                    qualifyingSetup.DurationSeconds);
            }
        }

        private void demoLapStreamToolStripMenuItem_Click(object sender, EventArgs e)
        {
            demoLapStreamToolStripMenuItem.Enabled = false;
            try
            {
                demoLapStreamToolStripMenuItem.Checked = s.ToggleDemoLapStream();
            }
            finally
            {
                System.Windows.Forms.Timer reenableTimer = new()
                {
                    Interval = 400
                };
                reenableTimer.Tick += (_, _) =>
                {
                    reenableTimer.Stop();
                    reenableTimer.Dispose();
                    demoLapStreamToolStripMenuItem.Enabled = true;
                };
                reenableTimer.Start();
            }
        }

        public void SetDemoLapStreamChecked(bool checkedState)
        {
            RunOnUiThread(() => demoLapStreamToolStripMenuItem.Checked = checkedState);
        }

        private bool ConfirmAbandonQualifying()
        {
            return !s.QualifyingActive ||
                MessageBox.Show(
                    this,
                    "Changing modes will discard the current qualifying session.",
                    "Discard Qualifying?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void SetPracticeMode()
        {
            SetRaceTitle(null);
            SetQualifyingAvailable(false);
            practiceToolStripMenuItem.Checked = true;
            heatRaceToolStripMenuItem.Checked = false;
        }

        private void SetHeatRaceMode()
        {
            practiceToolStripMenuItem.Checked = false;
            heatRaceToolStripMenuItem.Checked = true;
        }

        public void SetRaceTitle(string? raceName)
        {
            RunOnUiThread(() =>
            {
                Text = string.IsNullOrWhiteSpace(raceName)
                    ? DefaultWindowTitle
                    : $"{DefaultWindowTitle} - {raceName.Trim()}";
            });
        }

        public void SetQualifyingAvailable(bool available)
        {
            RunOnUiThread(() => qualifyingToolStripMenuItem.Enabled = available);
        }

        public void UpdateQualifyingStatus(
            int qualifierNumber,
            int qualifierCount,
            string state,
            TimeSpan remaining,
            string racerName)
        {
            RunOnUiThread(() =>
            {
                _heatStatusLabel.Text = $"Qualifying {qualifierNumber}/{qualifierCount} {state}";
                _heatTimerLabel.Text = $"Timer {FormatClock(remaining)}";
                _onDeckLabel.Text = $"Qualifier: {racerName}";
            });
        }

        public void ShowQualifyingLaneSelection(
            IReadOnlyList<QualifyingResult> rankedResults,
            Action<IReadOnlyList<string>> completed)
        {
            RunOnUiThread(() =>
            {
                using QualifyingLaneSelection selection = new(
                    rankedResults,
                    ActiveLaneCount,
                    LaneConfigurations);
                if (selection.ShowDialog(this) == DialogResult.OK)
                {
                    completed(selection.SeededRacers);
                }
            });
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
            ConfigureHeatStatusLayout();
            _boardHeaderLabels = new[] { racerHeaderLabel, lapsHeaderLabel, lastLapHeaderLabel, bestLapHeaderLabel, medianHeaderLabel, mphHeaderLabel };
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

            titleLabel.AutoSize = false;
            titleLabel.TextAlign = ContentAlignment.MiddleCenter;
            ApplyBoardFonts();
        }

        private void MKTS_Load(object sender, EventArgs e)
        {
            ApplyBoardFonts();
        }

        private void ApplyActiveLaneLayout()
        {
            for (int lane = 0; lane < LapProtocolParser.LaneCount; lane++)
            {
                RowStyle row = timingBoardLayout.RowStyles[lane + 1];
                row.SizeType = lane < ActiveLaneCount ? SizeType.Percent : SizeType.Absolute;
                row.Height = lane < ActiveLaneCount ? 100F / ActiveLaneCount : 0F;
            }

            timingBoardLayout.PerformLayout();
        }

        private void ApplyLaneColors()
        {
            Label[][] valueLabels =
            {
                _lapLabels,
                _lastLapLabels,
                _bestLapLabels,
                _medianLapLabels,
                _mphLabels
            };

            for (int lane = 0; lane < LapProtocolParser.LaneCount; lane++)
            {
                Color background = LaneConfigurations[lane].Color;
                Color foreground = GetContrastingTextColor(background);
                for (int column = 1; column < timingBoardLayout.ColumnCount; column++)
                {
                    if (timingBoardLayout.GetControlFromPosition(column, lane + 1) is Control cell)
                    {
                        cell.BackColor = background;
                    }

                    valueLabels[column - 1][lane].ForeColor = foreground;
                }
            }

            timingBoardLayout.Invalidate();
        }

        private static Color GetContrastingTextColor(Color background)
        {
            double luminance =
                (0.299 * background.R) +
                (0.587 * background.G) +
                (0.114 * background.B);
            return luminance >= 150 ? Color.Black : Color.White;
        }

        private void MKTS_Resize(object sender, EventArgs e)
        {
            ApplyBoardFonts();
        }

        private void ApplyBoardFonts()
        {
            foreach (Label label in _boardHeaderLabels)
            {
                SetFontSizeToFit(label, Math.Min(label.Height * 0.45f, 28f));
            }

            foreach (Label label in _boardValueLabels)
            {
                ApplyBoardValueFont(label);
            }

            foreach (Label label in _nameLabels)
            {
                ApplyRacerNameFont(label);
            }

            SetFontSizeToFit(titleLabel, titleLabel.Height * 0.4f);
        }

        private void ConfigureHeatStatusLayout()
        {
            if (_heatStatusLabel != null)
            {
                return;
            }

            TableLayoutPanel heatStatusPanel = new()
            {
                BackColor = Color.FromArgb(32, 32, 32),
                ColumnCount = 3,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(8, 2, 8, 2)
            };
            heatStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            heatStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            heatStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));

            _heatStatusLabel = CreateHeatStatusLabel("Practice");
            _heatTimerLabel = CreateHeatStatusLabel("Timer --:--");
            _onDeckLabel = CreateHeatStatusLabel("On deck: ");
            heatStatusPanel.Controls.Add(_heatStatusLabel, 0, 0);
            heatStatusPanel.Controls.Add(_heatTimerLabel, 1, 0);
            heatStatusPanel.Controls.Add(_onDeckLabel, 2, 0);

            mainLayoutPanel.SuspendLayout();
            mainLayoutPanel.Controls.Remove(titleLabel);
            mainLayoutPanel.RowStyles.Clear();
            mainLayoutPanel.RowCount = 3;
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 80F));
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            mainLayoutPanel.Controls.Add(heatStatusPanel, 0, 1);
            mainLayoutPanel.Controls.Add(titleLabel, 0, 2);
            mainLayoutPanel.ResumeLayout();
        }

        private static Label CreateHeatStatusLabel(string text) =>
            new()
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point),
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            };

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

        public void SetLaneRacerNames(IReadOnlyList<string> racerNames)
        {
            RunOnUiThread(() =>
            {
                for (int i = 0; i < _nameLabels.Length; i++)
                {
                    string racerName = i < racerNames.Count
                        ? racerNames[i]?.Trim() ?? string.Empty
                        : string.Empty;
                    _nameLabels[i].Text = string.IsNullOrWhiteSpace(racerName) ? EmptyRacerName : racerName;
                    ApplyRacerNameFont(_nameLabels[i]);
                }
            });
        }

        public void ResetHeatTimingDisplay(IReadOnlyList<int> lapCounts)
        {
            RunOnUiThread(() =>
            {
                for (int i = 0; i < LapProtocolParser.LaneCount; i++)
                {
                    int lapCount = i < lapCounts.Count ? Math.Max(0, lapCounts[i]) : 0;
                    _lapLabels[i].Text = FormatLapCount(lapCount);
                    _lastLapLabels[i].Text = string.Empty;
                    _bestLapLabels[i].Text = string.Empty;
                    _medianLapLabels[i].Text = string.Empty;
                    _mphLabels[i].Text = string.Empty;
                    ApplyBoardValueFont(_lapLabels[i]);
                }
            });
        }

        public void UpdateHeatRaceStatus(
            int heatNumber,
            int totalHeats,
            string state,
            TimeSpan remaining,
            string onDeckRacer)
        {
            RunOnUiThread(() =>
            {
                _heatStatusLabel.Text = heatNumber > 0 ? $"Heat {heatNumber}/{totalHeats} {state}" : state;
                _heatTimerLabel.Text = $"Timer {FormatClock(remaining)}";
                _onDeckLabel.Text = string.IsNullOrWhiteSpace(onDeckRacer) ? "On deck: " : $"On deck: {onDeckRacer}";
            });
        }

        public void ClearHeatRaceStatus()
        {
            RunOnUiThread(() =>
            {
                _heatStatusLabel.Text = "Practice";
                _heatTimerLabel.Text = "Timer --:--";
                _onDeckLabel.Text = "On deck: ";
            });
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
                _lapLabels[laneIndex].Text = FormatLapCount(lapCount);
                _lastLapLabels[laneIndex].Text = lastLap;
                _bestLapLabels[laneIndex].Text = bestLap;
                _medianLapLabels[laneIndex].Text = medianLap;
                _mphLabels[laneIndex].Text = milesPerHour;
                ApplyBoardValueFont(_lapLabels[laneIndex]);
                ApplyBoardValueFont(_lastLapLabels[laneIndex]);
                ApplyBoardValueFont(_bestLapLabels[laneIndex]);
                ApplyBoardValueFont(_medianLapLabels[laneIndex]);
                ApplyBoardValueFont(_mphLabels[laneIndex]);
            });
        }

        public void ShowLaneBaseline(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= LapProtocolParser.LaneCount)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                _lapLabels[laneIndex].Text = "0";
                _lastLapLabels[laneIndex].Text = string.Empty;
                _bestLapLabels[laneIndex].Text = string.Empty;
                _medianLapLabels[laneIndex].Text = string.Empty;
                _mphLabels[laneIndex].Text = string.Empty;
                ApplyBoardValueFont(_lapLabels[laneIndex]);
            });
        }

        public void SetStatusMessage(string message)
        {
            RunOnUiThread(() => statusLabel.Text = message);
        }

        private void ResetLaneDisplayCore(int laneIndex, bool clearRacer)
        {
            if (clearRacer)
            {
                _nameLabels[laneIndex].Text = EmptyRacerName;
            }

            _lapLabels[laneIndex].Text = string.Empty;
            _lastLapLabels[laneIndex].Text = string.Empty;
            _bestLapLabels[laneIndex].Text = string.Empty;
            _medianLapLabels[laneIndex].Text = string.Empty;
            _mphLabels[laneIndex].Text = string.Empty;
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

        private static void ApplyRacerNameFont(Label label)
        {
            const float maximumRacerNameSize = 32f;
            SetFontSizeToFit(label, Math.Min(label.Height * 0.4f, maximumRacerNameSize));
        }

        private static void ApplyBoardValueFont(Label label)
        {
            const float maximumBoardValueSize = 32f;
            SetFontSizeToFit(label, Math.Min(label.Height * 0.42f, maximumBoardValueSize));
        }

        private static string FormatClock(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
            {
                time = TimeSpan.Zero;
            }

            return time.TotalHours >= 1
                ? time.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
                : time.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatLapCount(int lapCount) =>
            lapCount > 0 ? lapCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

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
            racerContextMenu = new ContextMenuStrip
            {
                Tag = nameLabel
            };

            foreach (string racerName in LoadRacerNames())
            {
                racerContextMenu.Items.Add(racerName);
            }

            racerContextMenu.ItemClicked += racerNameMenu_ItemClicked;
            racerContextMenu.Show(Cursor.Position);
        }

        private static List<string> LoadRacerNames()
            => AppDatabase.LoadRacerNames();

        private void racerNameMenu_ItemClicked(object? sender, ToolStripItemClickedEventArgs e)
        {
            if (sender is ContextMenuStrip { Tag: Label nameLabel })
            {
                nameLabel.Text = e.ClickedItem?.Text ?? string.Empty;
                ApplyRacerNameFont(nameLabel);
            }
        }

        private void configureToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using Configure config = new Configure(
                MinLapMilliseconds,
                SoundOnTooFastLap,
                port,
                SpeechVoiceName,
                ActiveLaneCount,
                TrackLengthFeet,
                SensorDebounceMilliseconds,
                RawSensorLockoutMilliseconds,
                LaneConfigurations);
            if (config.ShowDialog(this) == DialogResult.OK)
            {
                MinLapMilliseconds = config.MinLapMilliseconds;
                SoundOnTooFastLap = config.SoundOnTooFastLap;
                SpeechVoiceName = config.SelectedSpeechVoice;
                ActiveLaneCount = config.ActiveLaneCount;
                TrackLengthFeet = config.TrackLengthFeet;
                SensorDebounceMilliseconds = config.SensorDebounceMilliseconds;
                RawSensorLockoutMilliseconds = config.RawSensorLockoutMilliseconds;
                LaneConfigurations = config.LaneConfigurations;
                ApplyLaneColors();
                ApplyActiveLaneLayout();
                AppDatabase.SaveAppSettings(new AppSettings(
                    MinLapMilliseconds,
                    SoundOnTooFastLap,
                    SpeechVoiceName,
                    ActiveLaneCount));
                AppDatabase.SaveLaneConfigurations(LaneConfigurations);
                AppDatabase.SaveTrackLengthFeet(TrackLengthFeet);
                AppDatabase.SaveSensorDebounceMilliseconds(SensorDebounceMilliseconds);
                AppDatabase.SaveRawSensorLockoutMilliseconds(RawSensorLockoutMilliseconds);
                SpeechAnnouncer.WarmUpAsync(SpeechVoiceName);
                s.ApplySettings();
                s.SetPort(config.SelectedPort);
            }
        }

    }
}
