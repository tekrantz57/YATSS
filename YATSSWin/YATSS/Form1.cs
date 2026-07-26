using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace YATSS
{
    public partial class YATSS : Form
    {
        private const uint EsContinuous = 0x80000000;
        private const uint EsSystemRequired = 0x00000001;
        private const uint EsDisplayRequired = 0x00000002;
        static Serial s = null!;
        private Label[] _boardValueLabels = Array.Empty<Label>();
        private Label[] _boardHeaderLabels = Array.Empty<Label>();
        private Label[] _nameLabels = Array.Empty<Label>();
        private Label[] _lapLabels = Array.Empty<Label>();
        private Label[] _totalLapLabels = Array.Empty<Label>();
        private Label[] _lastLapLabels = Array.Empty<Label>();
        private Label[] _bestLapLabels = Array.Empty<Label>();
        private Label[] _medianLapLabels = Array.Empty<Label>();
        private int[] _heatStartingLapCounts = new int[LapProtocolParser.LaneCount];
        private bool _showHeatLapCounts;
        private Label _heatStatusLabel = null!;
        private Label _heatTimerLabel = null!;
        private Label _onDeckLabel = null!;
        private readonly System.Windows.Forms.Timer _practiceClockTimer = new();
        private bool _practiceClockEnabled = true;
        private ControllerDiagnosticsForm? _controllerDiagnosticsForm;
        private readonly ToolStripMenuItem _backupDatabaseMenuItem = new("Back Up Database...");
        private readonly ToolStripMenuItem _restoreDatabaseMenuItem = new("Restore Database...");
        private readonly ToolStripMenuItem _openDatabaseFolderMenuItem = new("Open Database Folder");
        private readonly ToolStripMenuItem _openBackupFolderMenuItem = new("Open Backup Folder");
        private const string EmptyRacerName = "          ";
        private const string DefaultWindowTitle = "YATSS";
        public string port = "";
        public int MinLapMilliseconds { get; private set; } = LapRaceOptions.Default.MinLapMilliseconds;
        public bool SoundOnTooFastLap { get; private set; } = true;
        public string SpeechVoiceName { get; private set; } = "";
        public int ActiveLaneCount { get; private set; } = LapProtocolParser.LaneCount;
        public double TrackLengthFeet { get; private set; } = LapRaceOptions.Default.TrackLengthFeet;
        public int SensorDebounceMilliseconds { get; private set; } = AppDatabase.DefaultSensorDebounceMilliseconds;
        public int RawSensorLockoutMilliseconds { get; private set; } = AppDatabase.DefaultRawSensorLockoutMilliseconds;
        public bool ExportRaceJson { get; private set; } = true;
        public bool ExportRaceCsv { get; private set; } = true;
        public IReadOnlyList<LaneConfiguration> LaneConfigurations { get; private set; } =
            LaneConfiguration.CreateDefaults();

        public YATSS()
        {
            InitializeComponent();
            ConfigureDataMenu();
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
            RaceReportSettings reportSettings = AppDatabase.LoadRaceReportSettings(
                new RaceReportSettings(ExportJson: true, ExportCsv: true));
            ExportRaceJson = reportSettings.ExportJson;
            ExportRaceCsv = reportSettings.ExportCsv;
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

            KeepSystemAwake();
            s = new Serial(this);
            ConfigurePracticeClock();
            WireBestLapResetClicks();
            FormClosed += (_, _) =>
            {
                _practiceClockTimer.Stop();
                _practiceClockTimer.Dispose();
                s.Dispose();
                AllowSystemSleep();
            };
            Shown += (_, _) => BeginInvoke(CreateAutomaticDatabaseBackup);
        }

        private void ConfigureDataMenu()
        {
            ToolStripMenuItem dataMenu = new("Data");
            dataMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                _backupDatabaseMenuItem,
                _restoreDatabaseMenuItem,
                new ToolStripSeparator(),
                _openDatabaseFolderMenuItem,
                _openBackupFolderMenuItem
            });

            int fileMenuIndex = menuStrip1.Items.IndexOf(fileToolStripMenuItem);
            menuStrip1.Items.Insert(fileMenuIndex + 1, dataMenu);
            _backupDatabaseMenuItem.Click += (_, _) => BackUpDatabase();
            _restoreDatabaseMenuItem.Click += (_, _) => RestoreDatabase();
            _openDatabaseFolderMenuItem.Click += (_, _) => OpenDatabaseFolder();
            _openBackupFolderMenuItem.Click += (_, _) => OpenBackupFolder();
        }

        private void BackUpDatabase()
        {
            try
            {
                string backupDirectory = AppDatabase.GetDefaultBackupDirectory();
                Directory.CreateDirectory(backupDirectory);
                using SaveFileDialog dialog = new()
                {
                    Title = "Back Up YATSS Database",
                    InitialDirectory = backupDirectory,
                    FileName = $"YATSS-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db",
                    Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
                    DefaultExt = "db",
                    AddExtension = true,
                    OverwritePrompt = true,
                    RestoreDirectory = true
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                UseWaitCursor = true;
                DatabaseBackupResult result = AppDatabase.CreateBackup(dialog.FileName);
                MessageBox.Show(
                    this,
                    $"Database backup verified and saved.\n\n" +
                    $"Racers: {result.RacerCount}\n\n" +
                    result.Path,
                    "Backup Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Database Backup Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void RestoreDatabase()
        {
            if (!s.CanRestoreDatabase(out string reason))
            {
                MessageBox.Show(
                    this,
                    reason,
                    "Database Restore Unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                string backupDirectory = AppDatabase.GetDefaultBackupDirectory();
                Directory.CreateDirectory(backupDirectory);
                using OpenFileDialog dialog = new()
                {
                    Title = "Restore YATSS Database",
                    InitialDirectory = backupDirectory,
                    Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false,
                    RestoreDirectory = true
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                DialogResult confirmation = MessageBox.Show(
                    this,
                    "Restore the selected database?\n\n" +
                    "The current database will be backed up automatically before it is replaced. " +
                    "YATSS will restart after the restore.",
                    "Confirm Database Restore",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes)
                {
                    return;
                }

                string safetyBackupPath = Path.Combine(
                    backupDirectory,
                    $"YATSS-before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");
                CloseControllerDiagnostics();
                s.PrepareForDatabaseRestore();
                UseWaitCursor = true;
                DatabaseRestoreResult result = AppDatabase.RestoreBackup(
                    dialog.FileName,
                    safetyBackupPath);

                MessageBox.Show(
                    this,
                    $"Database restored and verified.\n\n" +
                    $"Racers: {result.RacerCount}\n\n" +
                    $"Previous database saved to:\n{result.SafetyBackupPath}\n\n" +
                    "YATSS will now restart.",
                    "Restore Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Application.Restart();
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Database Restore Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void CreateAutomaticDatabaseBackup()
        {
            try
            {
                _ = AppDatabase.CreateAutomaticBackup();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    "YATSS could not create today's automatic database backup.\n\n" +
                    exception.Message,
                    "Automatic Backup Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void OpenDatabaseFolder()
        {
            string directory = Path.GetDirectoryName(AppDatabase.DatabasePath)
                ?? throw new InvalidOperationException("The database folder could not be found.");
            OpenFolder(directory, "Database Folder Could Not Be Opened");
        }

        private void OpenBackupFolder()
        {
            OpenFolder(
                AppDatabase.GetDefaultBackupDirectory(),
                "Backup Folder Could Not Be Opened");
        }

        private void OpenFolder(string directory, string errorTitle)
        {
            try
            {
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    errorTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private static void KeepSystemAwake()
        {
            SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
        }

        private static void AllowSystemSleep()
        {
            SetThreadExecutionState(EsContinuous);
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
            Close();
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseControllerDiagnostics();
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

        private void controllerDiagnosticsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_controllerDiagnosticsForm is { IsDisposed: false })
            {
                _controllerDiagnosticsForm.Activate();
                return;
            }

            if (!s.CanStartControllerDiagnostics(out string reason))
            {
                SetStatusMessage(reason);
                MessageBox.Show(this, reason, "Controller Diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ControllerDiagnosticsForm diagnostics = new(
                port,
                LaneConfigurations,
                s.RequestDiagnosticStatus,
                s.ClearDiagnosticCounts,
                lane => s.PulseDiagnosticRelay(lane),
                s.CutAllPowerDuringDiagnostics);
            _controllerDiagnosticsForm = diagnostics;
            s.DiagnosticReceived += diagnostics.ApplyDiagnostic;
            diagnostics.FormClosed += (_, _) =>
            {
                s.DiagnosticReceived -= diagnostics.ApplyDiagnostic;
                s.StopControllerDiagnostics();
                if (ReferenceEquals(_controllerDiagnosticsForm, diagnostics))
                {
                    _controllerDiagnosticsForm = null;
                }
            };
            diagnostics.Show(this);

            if (!s.StartControllerDiagnostics(out reason))
            {
                diagnostics.Close();
                SetStatusMessage(reason);
            }
        }

        private void CloseControllerDiagnostics()
        {
            if (_controllerDiagnosticsForm is { IsDisposed: false } diagnostics)
            {
                diagnostics.Close();
            }
            else
            {
                s.StopControllerDiagnostics();
            }
        }

        private void practiceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ConfirmAbandonQualifying())
            {
                return;
            }

            CloseControllerDiagnostics();
            SetPracticeMode();
            s.SetPracticeMode();
        }

        private void heatRaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ConfirmAbandonQualifying())
            {
                return;
            }

            CloseControllerDiagnostics();
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
            CloseControllerDiagnostics();
            using QualifyingSetup qualifyingSetup = new(ActiveLaneCount, LaneConfigurations);
            if (qualifyingSetup.ShowDialog(this) == DialogResult.OK)
            {
                s.ConfigureQualifying(
                    qualifyingSetup.LaneIndex,
                    qualifyingSetup.DurationSeconds);
            }
        }

        private void demoRaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ConfirmAbandonQualifying())
            {
                return;
            }

            CloseControllerDiagnostics();
            const string demoRaceName = "Demo Race";
            const int demoHeatLengthMinutes = 1;
            const int demoBetweenHeatsSeconds = 5;
            string[] demoRacers = CreateDemoRacerNames();

            s.CancelQualifying();
            SetHeatRaceMode();
            s.ConfigureHeatRace(
                demoRaceName,
                demoHeatLengthMinutes,
                demoBetweenHeatsSeconds,
                demoRacers,
                ActiveLaneCount,
                LaneConfigurations,
                TrackLengthFeet);
            SetRaceTitle(demoRaceName);
            SetLaneRacerNames(GetFirstHeatLaneRacers(demoRacers));
            SetQualifyingAvailable(false);
            s.StartDemoLapStream();
            SetStatusMessage("Demo race ready. Press Space to start.");
        }

        private void demoLapStreamToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseControllerDiagnostics();
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

        private string[] CreateDemoRacerNames()
        {
            int racerCount = Math.Clamp(ActiveLaneCount + 2, 2, 12);
            return Enumerable.Range(1, racerCount)
                .Select(index => $"Demo Racer {index}")
                .ToArray();
        }

        private string[] GetFirstHeatLaneRacers(IReadOnlyList<string> racers)
        {
            string[] laneRacers = new string[LapProtocolParser.LaneCount];
            IReadOnlyList<int> firstHeatLaneIndexes = HeatRaceController.GetInitialLaneIndexes(ActiveLaneCount);
            for (int i = 0; i < racers.Count && i < firstHeatLaneIndexes.Count; i++)
            {
                laneRacers[firstHeatLaneIndexes[i]] = racers[i];
            }

            return laneRacers;
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
                SetPracticeClockEnabled(false);
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
            _nameLabels = new[] { name0, name1, name2, name3, name4, name5, name6, name7 };
            _lapLabels = new[] { laps0, laps1, laps2, laps3, laps4, laps5, laps6, laps7 };
            _totalLapLabels = new[] { mph0, mph1, mph2, mph3, mph4, mph5, mph6, mph7 };
            _lastLapLabels = new[] { ll0, ll1, ll2, ll3, ll4, ll5, ll6, ll7 };
            _bestLapLabels = new[] { bl0, bl1, bl2, bl3, bl4, bl5, bl6, bl7 };
            _medianLapLabels = new[] { ml0, ml1, ml2, ml3, ml4, ml5, ml6, ml7 };
            ConfigureBoardColumns();
            _boardHeaderLabels = new[] { racerHeaderLabel, mphHeaderLabel, lapsHeaderLabel, bestLapHeaderLabel, medianHeaderLabel, lastLapHeaderLabel };
            _boardValueLabels = new[]
            {
                name0, mph0, laps0, bl0, ml0, ll0,
                name1, mph1, laps1, bl1, ml1, ll1,
                name2, mph2, laps2, bl2, ml2, ll2,
                name3, mph3, laps3, bl3, ml3, ll3,
                name4, mph4, laps4, bl4, ml4, ll4,
                name5, mph5, laps5, bl5, ml5, ll5,
                name6, mph6, laps6, bl6, ml6, ll6,
                name7, mph7, laps7, bl7, ml7, ll7
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

        private void ConfigureBoardColumns()
        {
            mphHeaderLabel.Text = "Total Laps";
            lapsHeaderLabel.Text = "Laps";
            lastLapHeaderLabel.Text = "Last Lap";

            timingBoardLayout.SetColumn(mphHeaderLabel, 1);
            timingBoardLayout.SetColumn(lapsHeaderLabel, 2);
            timingBoardLayout.SetColumn(bestLapHeaderLabel, 3);
            timingBoardLayout.SetColumn(medianHeaderLabel, 4);
            timingBoardLayout.SetColumn(lastLapHeaderLabel, 5);

            MoveColumnCells(_totalLapLabels, 1);
            MoveColumnCells(_lapLabels, 2);
            MoveColumnCells(_bestLapLabels, 3);
            MoveColumnCells(_medianLapLabels, 4);
            MoveColumnCells(_lastLapLabels, 5);
        }

        private void MoveColumnCells(IEnumerable<Label> labels, int column)
        {
            foreach (Label label in labels)
            {
                if (label.Parent != null)
                {
                    timingBoardLayout.SetColumn(label.Parent, column);
                }
            }
        }

        private void YATSS_Load(object sender, EventArgs e)
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
                _totalLapLabels,
                _lapLabels,
                _bestLapLabels,
                _medianLapLabels,
                _lastLapLabels
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

        private void YATSS_Resize(object sender, EventArgs e)
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
                Margin = new Padding(3),
                Padding = new Padding(8, 0, 8, 0),
                RowCount = 1
            };
            heatStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            heatStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            heatStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            heatStatusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

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

        private void ConfigurePracticeClock()
        {
            _practiceClockTimer.Interval = 1000;
            _practiceClockTimer.Tick += (_, _) => UpdateStatusTimer();
            _practiceClockTimer.Start();
            UpdateStatusTimer();
        }

        private void SetPracticeClockEnabled(bool enabled)
        {
            _practiceClockEnabled = enabled;
            if (enabled)
            {
                UpdatePracticeClock();
            }
        }

        private void UpdatePracticeClock()
        {
            if (!_practiceClockEnabled || _heatTimerLabel == null || IsDisposed)
            {
                return;
            }

            _heatTimerLabel.Text = DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
        }

        private void UpdateStatusTimer()
        {
            if (_practiceClockEnabled)
            {
                UpdatePracticeClock();
                return;
            }

            s.RefreshActiveStatus();
        }

        private static Label CreateHeatStatusLabel(string text) =>
            new()
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point),
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            };

        public void ResetBoardDisplay(bool clearRacers)
        {
            RunOnUiThread(() =>
            {
                _showHeatLapCounts = false;
                Array.Clear(_heatStartingLapCounts);
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
                _showHeatLapCounts = true;
                for (int i = 0; i < LapProtocolParser.LaneCount; i++)
                {
                    int lapCount = i < lapCounts.Count ? Math.Max(0, lapCounts[i]) : 0;
                    _heatStartingLapCounts[i] = lapCount;
                    _totalLapLabels[i].Text = FormatLapCount(lapCount);
                    _lapLabels[i].Text = string.Empty;
                    _lastLapLabels[i].Text = string.Empty;
                    _bestLapLabels[i].Text = string.Empty;
                    _medianLapLabels[i].Text = string.Empty;
                    ApplyBoardValueFont(_totalLapLabels[i]);
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
                SetPracticeClockEnabled(false);
                string trimmedState = state.Trim();
                bool waitingForSpace = string.Equals(trimmedState, "Ready", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmedState, "Paused", StringComparison.OrdinalIgnoreCase);
                string displayState = trimmedState switch
                {
                    "Ready" => "PRESS SPACE TO START",
                    "Paused" => "PAUSED - PRESS SPACE TO RESUME",
                    "Starting" => "STARTING...",
                    "Resuming" => "RESUMING...",
                    _ => trimmedState
                };
                _heatStatusLabel.Text = heatNumber > 0 && totalHeats > 0
                    ? string.IsNullOrWhiteSpace(displayState)
                        ? $"Heat {heatNumber}/{totalHeats}"
                        : $"Heat {heatNumber}/{totalHeats} - {displayState}"
                    : displayState;
                _heatStatusLabel.ForeColor = waitingForSpace ? Color.Gold : Color.White;
                SetFontSizeToFit(_heatStatusLabel, 14F);
                string timerPrefix = string.Equals(trimmedState, "Intermission", StringComparison.OrdinalIgnoreCase)
                    ? "Next"
                    : "Timer";
                _heatTimerLabel.Text = $"{timerPrefix} {FormatClock(remaining)}";
                _onDeckLabel.Text = string.IsNullOrWhiteSpace(onDeckRacer) ? "On deck: " : $"On deck: {onDeckRacer}";
            });
        }

        public void ClearHeatRaceStatus()
        {
            RunOnUiThread(() =>
            {
                _heatStatusLabel.Text = "Practice";
                _heatStatusLabel.ForeColor = Color.White;
                SetPracticeClockEnabled(true);
                _onDeckLabel.Text = "On deck: ";
            });
        }

        public void UpdateLaneDisplay(
            int laneIndex,
            int lapCount,
            string lastLap,
            string bestLap,
            string medianLap)
        {
            if (laneIndex < 0 || laneIndex >= LapProtocolParser.LaneCount)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                _totalLapLabels[laneIndex].Text = FormatLapCount(lapCount);
                _lapLabels[laneIndex].Text = _showHeatLapCounts
                    ? FormatLapCount(Math.Max(0, lapCount - _heatStartingLapCounts[laneIndex]))
                    : string.Empty;
                _lastLapLabels[laneIndex].Text = lastLap;
                _bestLapLabels[laneIndex].Text = bestLap;
                _medianLapLabels[laneIndex].Text = medianLap;
                ApplyBoardValueFont(_totalLapLabels[laneIndex]);
                ApplyBoardValueFont(_lapLabels[laneIndex]);
                ApplyBoardValueFont(_lastLapLabels[laneIndex]);
                ApplyBoardValueFont(_bestLapLabels[laneIndex]);
                ApplyBoardValueFont(_medianLapLabels[laneIndex]);
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
                _totalLapLabels[laneIndex].Text = "0";
                _lapLabels[laneIndex].Text = string.Empty;
                _lastLapLabels[laneIndex].Text = string.Empty;
                _bestLapLabels[laneIndex].Text = string.Empty;
                _medianLapLabels[laneIndex].Text = string.Empty;
                ApplyBoardValueFont(_totalLapLabels[laneIndex]);
            });
        }

        public void SetStatusMessage(string message)
        {
            RunOnUiThread(() => statusLabel.Text = message);
        }

        public void ShowHeatRaceReport(string path)
        {
            RunOnUiThread(() =>
            {
                HeatRaceReportForm reportForm = new(path);
                reportForm.Show(this);
            });
        }

        private void ResetLaneDisplayCore(int laneIndex, bool clearRacer)
        {
            if (clearRacer)
            {
                _nameLabels[laneIndex].Text = EmptyRacerName;
            }

            _totalLapLabels[laneIndex].Text = string.Empty;
            _lapLabels[laneIndex].Text = string.Empty;
            _lastLapLabels[laneIndex].Text = string.Empty;
            _bestLapLabels[laneIndex].Text = string.Empty;
            _medianLapLabels[laneIndex].Text = string.Empty;
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

        internal static string FormatClock(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
            {
                time = TimeSpan.Zero;
            }

            long totalSeconds = (long)time.TotalSeconds;
            long hours = totalSeconds / 3600;
            int minutes = (int)(totalSeconds % 3600) / 60;
            int seconds = (int)(totalSeconds % 60);
            return hours >= 1
                ? FormattableString.Invariant($"{hours}:{minutes:00}:{seconds:00}")
                : FormattableString.Invariant($"{minutes}:{seconds:00}");
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
            if (Control.MouseButtons != MouseButtons.Left)
            {
                return;
            }

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
                ExportRaceJson,
                ExportRaceCsv,
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
                ExportRaceJson = config.ExportRaceJson;
                ExportRaceCsv = config.ExportRaceCsv;
                LaneConfigurations = config.LaneConfigurations;
                ApplyLaneColors();
                ApplyActiveLaneLayout();
                AppDatabase.SaveAppSettings(new AppSettings(
                    MinLapMilliseconds,
                    SoundOnTooFastLap,
                    SpeechVoiceName,
                    ActiveLaneCount));
                AppDatabase.SaveRaceReportSettings(new RaceReportSettings(
                    ExportRaceJson,
                    ExportRaceCsv));
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
