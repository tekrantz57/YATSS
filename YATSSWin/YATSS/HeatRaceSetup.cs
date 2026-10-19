namespace YATSS
{
    public sealed class HeatRaceSetup : Form
    {
        internal const int DefaultBetweenHeatsSeconds = 60;
        private readonly CheckedListBox _racerList = new();
        private readonly ListBox _selectedRacers = new();
        private readonly DataGridView _laneGrid = new();
        private readonly Label _queueLabel = new();
        private readonly NumericUpDown _heatLengthSeconds = new();
        private readonly NumericUpDown _betweenHeatsSeconds = new();
        private readonly NumericUpDown _lapTarget = new() { Minimum = 1, Maximum = HeatRaceController.MaximumLapTarget, Value = 20, Width = 80 };
        private readonly Label _lapTargetLabel = new() { Text = "Target laps", AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
        private Label _heatLengthLabel = null!, _betweenHeatsLabel = null!;
        private readonly TextBox _raceName = new();
        private readonly ComboBox _format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        private readonly ComboBox _scheduling = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private HeatRaceScheduling _ordinaryScheduling;
        public RaceFormat Format => (RaceFormat)Math.Max(0, _format.SelectedIndex);
        public HeatRaceScheduling Scheduling => Format == RaceFormat.CombinedDistance
            ? HeatRaceScheduling.IndependentGroups : Format == RaceFormat.LapFinish ? HeatRaceScheduling.RotationQueue : _ordinaryScheduling;
        private readonly Button _okButton = new();
        private readonly Random _random = new();
        private readonly List<string> _selectedNames = new();
        private readonly TableLayoutPanel _heatLayout = new();
        private readonly GroupBox _selectedGroup = new() { Dock = DockStyle.Fill };
        private readonly int _activeLaneCount;
        private readonly IReadOnlyList<int> _initialLaneIndexes;
        private readonly IReadOnlyList<int> _rotationLaneIndexes;
        private readonly string[] _laneNames;
        private readonly Color[] _laneColors;
        private bool _loadingRacers;

        public int HeatLengthSeconds => (int)_heatLengthSeconds.Value;
        public int BetweenHeatsSeconds => (int)_betweenHeatsSeconds.Value;
        public int LapTarget => (int)_lapTarget.Value;
        public string RaceName => _raceName.Text.Trim();
        public IReadOnlyList<string> SelectedRacers => _selectedNames.ToArray();
        public IReadOnlyList<string> FirstHeatLaneRacers => GetFirstHeatLaneRacers();

        public HeatRaceSetup(
            int activeLaneCount,
            IReadOnlyList<LaneConfiguration> laneConfigurations)
            : this(activeLaneCount, laneConfigurations, LoadRacerNames(),
                AppDatabase.LoadHeatRaceSettings(new(180, DefaultBetweenHeatsSeconds, string.Empty)))
        { }

        internal HeatRaceSetup(int activeLaneCount, IReadOnlyList<LaneConfiguration> laneConfigurations,
            IReadOnlyList<string> racerNames, HeatRaceSetupSettings settings)
        {
            _activeLaneCount = Math.Clamp(activeLaneCount, 2, LapProtocolParser.LaneCount);
            _initialLaneIndexes = HeatRaceController.GetInitialLaneIndexes(_activeLaneCount);
            _rotationLaneIndexes = HeatRaceController.GetRotationLaneIndexes(_activeLaneCount);
            _laneNames = LaneConfiguration.CreateDefaults()
                .Select((lane, index) => index < laneConfigurations.Count
                    ? laneConfigurations[index].Name
                    : lane.Name)
                .ToArray();
            _laneColors = LaneConfiguration.CreateDefaults()
                .Select((lane, index) => index < laneConfigurations.Count
                    ? laneConfigurations[index].Color
                    : lane.Color)
                .ToArray();
            Text = "Heat Race";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(860, 560);

            BuildLayout();
            foreach (string name in racerNames) _racerList.Items.Add(name, false);
            LoadSettings(settings);
            UpdateSelection();
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            Controls.Add(root);

            GroupBox racersGroup = new()
            {
                Text = "Racers",
                Dock = DockStyle.Fill
            };
            root.Controls.Add(racersGroup, 0, 0);

            TableLayoutPanel racerLayout = new()
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                Padding = new Padding(8)
            };
            racerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            racerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
            racerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
            racersGroup.Controls.Add(racerLayout);

            _racerList.Dock = DockStyle.Fill;
            _racerList.CheckOnClick = true;
            _racerList.ItemCheck += (_, _) =>
            {
                if (!_loadingRacers)
                {
                    BeginInvoke(UpdateSelection);
                }
            };
            racerLayout.Controls.Add(_racerList, 0, 0);

            FlowLayoutPanel racerButtons = new()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight
            };
            racerLayout.Controls.Add(racerButtons, 0, 1);

            Button selectAllButton = new()
            {
                Text = "Select All",
                AutoSize = true
            };
            selectAllButton.Click += (_, _) => SetAllRacersChecked(true);
            racerButtons.Controls.Add(selectAllButton);

            Button clearButton = new()
            {
                Text = "Clear",
                AutoSize = true
            };
            clearButton.Click += (_, _) => SetAllRacersChecked(false);
            racerButtons.Controls.Add(clearButton);

            Button editUsersButton = new()
            {
                Text = "Add User...",
                AutoSize = true
            };
            editUsersButton.Click += (_, _) => OpenEditUsers();
            racerButtons.Controls.Add(editUsersButton);

            _queueLabel.Dock = DockStyle.Fill;
            _queueLabel.TextAlign = ContentAlignment.MiddleLeft;
            racerLayout.Controls.Add(_queueLabel, 0, 2);

            GroupBox heatGroup = new()
            {
                Text = "Race",
                Dock = DockStyle.Fill
            };
            root.Controls.Add(heatGroup, 1, 0);

            TableLayoutPanel heatLayout = _heatLayout;
            heatLayout.Dock = DockStyle.Fill;
            heatLayout.RowCount = 4;
            heatLayout.Padding = new Padding(8);
            heatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 102F));
            heatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
            heatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 70F));
            heatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30F));
            heatGroup.Controls.Add(heatLayout);

            TableLayoutPanel raceIdentity = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2, RowCount = 3
            };
            raceIdentity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            raceIdentity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 3; row++) raceIdentity.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3));
            heatLayout.Controls.Add(raceIdentity, 0, 0);

            raceIdentity.Controls.Add(new Label
            {
                Text = "Race name",
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            });
            _raceName.Dock = DockStyle.Fill;
            _raceName.MaxLength = 80;
            raceIdentity.Controls.Add(_raceName);
            _format.Items.AddRange(new object[] { "Heat Race", "Combined Distance", "Lap Finish" });
            _format.SelectedIndex = 0;
            raceIdentity.Controls.Add(new Label { Text = "Race format", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 1);
            _format.Dock = DockStyle.Fill;
            raceIdentity.Controls.Add(_format, 1, 1);
            _format.SelectedIndexChanged += (_, _) => { UpdateSchedulingControl(); RenderSelection(); };
            raceIdentity.Controls.Add(new Label { Text = "Scheduling", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 2);
            _scheduling.Items.AddRange(new object[] { "Rotation Queue", "Independent Groups" });
            _scheduling.SelectedIndex = 0;
            _scheduling.SelectedIndexChanged += (_, _) =>
            {
                if (Format == RaceFormat.HeatRace) _ordinaryScheduling = (HeatRaceScheduling)_scheduling.SelectedIndex;
                RenderSelection();
            };
            raceIdentity.Controls.Add(_scheduling, 1, 2);

            FlowLayoutPanel heatSettings = new()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            heatLayout.Controls.Add(heatSettings, 0, 1);

            Label heatLengthLabel = new()
            {
                Text = "Heat length (seconds)",
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            };
            heatSettings.Controls.Add(heatLengthLabel);
            _heatLengthLabel = heatLengthLabel;

            _heatLengthSeconds.Minimum = 1;
            _heatLengthSeconds.Maximum = HeatRaceController.MaximumHeatLengthSeconds;
            _heatLengthSeconds.Value = 180;
            _heatLengthSeconds.Width = 80;
            heatSettings.Controls.Add(_heatLengthSeconds);
            heatSettings.SetFlowBreak(_heatLengthSeconds, true);

            Label betweenHeatsLabel = new()
            {
                Text = "Between heats (seconds)",
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            };
            heatSettings.Controls.Add(betweenHeatsLabel);
            _betweenHeatsLabel = betweenHeatsLabel;

            _betweenHeatsSeconds.Minimum = 0;
            _betweenHeatsSeconds.Maximum = 300;
            _betweenHeatsSeconds.Value = DefaultBetweenHeatsSeconds;
            _betweenHeatsSeconds.Width = 64;
            heatSettings.Controls.Add(_betweenHeatsSeconds);
            heatSettings.Controls.Add(_lapTargetLabel);
            heatSettings.Controls.Add(_lapTarget);

            ConfigureLaneGrid();
            heatLayout.Controls.Add(_laneGrid, 0, 2);

            GroupBox selectedGroup = _selectedGroup;
            heatLayout.Controls.Add(selectedGroup, 0, 3);

            _selectedRacers.Dock = DockStyle.Fill;
            selectedGroup.Controls.Add(_selectedRacers);

            FlowLayoutPanel buttons = new()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            root.SetColumnSpan(buttons, 2);
            root.Controls.Add(buttons, 0, 1);

            Button cancelButton = new()
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true
            };
            buttons.Controls.Add(cancelButton);

            _okButton.Text = "OK";
            _okButton.DialogResult = DialogResult.OK;
            _okButton.AutoSize = true;
            buttons.Controls.Add(_okButton);

            Button randomizeButton = new()
            {
                Text = "Randomize",
                AutoSize = true
            };
            randomizeButton.Click += (_, _) => RandomizeSelectedRacers();
            buttons.Controls.Add(randomizeButton);

            AcceptButton = _okButton;
            CancelButton = cancelButton;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                SaveSettings();
            }

            base.OnFormClosing(e);
        }

        private void ConfigureLaneGrid()
        {
            _laneGrid.Dock = DockStyle.Fill;
            _laneGrid.AllowUserToAddRows = false;
            _laneGrid.AllowUserToDeleteRows = false;
            _laneGrid.AllowUserToResizeRows = false;
            _laneGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _laneGrid.BackgroundColor = SystemColors.Window;
            _laneGrid.MultiSelect = false;
            _laneGrid.ReadOnly = true;
            _laneGrid.RowHeadersVisible = false;
            _laneGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _laneGrid.Columns.Add("lane", "Lane");
            _laneGrid.Columns.Add("racer", "Racer");
            _laneGrid.Columns.Add("next", "Next");
        }

        private void LoadRacers()
        {
            _racerList.Items.Clear();
            foreach (string name in LoadRacerNames())
            {
                _racerList.Items.Add(name, false);
            }
        }

        private static List<string> LoadRacerNames()
            => AppDatabase.LoadRacerNames();

        private void LoadSettings(HeatRaceSetupSettings settings)
        {
            _ordinaryScheduling = settings.Scheduling;
            _lapTarget.Value = Math.Clamp(settings.LapTarget, 1, HeatRaceController.MaximumLapTarget);
            _format.SelectedIndex = (int)settings.Format;
            UpdateSchedulingControl();
            _heatLengthSeconds.Value = Math.Clamp(
                settings.HeatLengthSeconds,
                (int)_heatLengthSeconds.Minimum,
                (int)_heatLengthSeconds.Maximum);
            _betweenHeatsSeconds.Value = Math.Clamp(
                settings.BetweenHeatsSeconds,
                (int)_betweenHeatsSeconds.Minimum,
                (int)_betweenHeatsSeconds.Maximum);
            _raceName.Text = settings.RaceName;
        }

        private void SaveSettings()
        {
            AppDatabase.SaveHeatRaceSettings(new HeatRaceSetupSettings(
                HeatLengthSeconds,
                BetweenHeatsSeconds,
                RaceName) { Scheduling = _ordinaryScheduling, Format = Format, LapTarget = LapTarget });
        }

        private void SetAllRacersChecked(bool isChecked)
        {
            for (int i = 0; i < _racerList.Items.Count; i++)
            {
                _racerList.SetItemChecked(i, isChecked);
            }

            UpdateSelection();
        }

        private void OpenEditUsers()
        {
            List<string> selectedBeforeEdit = _selectedNames.ToList();
            using EditUsers editUsers = new();
            editUsers.ShowDialog(this);
            ReloadRacers(selectedBeforeEdit);
        }

        private void ReloadRacers(IReadOnlyList<string> selectedNames)
        {
            _loadingRacers = true;
            try
            {
                LoadRacers();
                HashSet<string> availableNames = new(StringComparer.OrdinalIgnoreCase);
                foreach (object item in _racerList.Items)
                {
                    if (item is string name)
                    {
                        availableNames.Add(name);
                    }
                }

                _selectedNames.Clear();
                foreach (string selectedName in selectedNames)
                {
                    if (availableNames.Contains(selectedName))
                    {
                        _selectedNames.Add(selectedName);
                    }
                }

                for (int i = 0; i < _racerList.Items.Count; i++)
                {
                    string? racerName = _racerList.Items[i]?.ToString();
                    bool isChecked = !string.IsNullOrWhiteSpace(racerName) &&
                        _selectedNames.Contains(racerName, StringComparer.OrdinalIgnoreCase);
                    _racerList.SetItemChecked(i, isChecked);
                }
            }
            finally
            {
                _loadingRacers = false;
            }

            RenderSelection();
        }

        private void RandomizeSelectedRacers()
        {
            for (int i = _selectedNames.Count - 1; i > 0; i--)
            {
                int swapIndex = _random.Next(i + 1);
                (_selectedNames[i], _selectedNames[swapIndex]) = (_selectedNames[swapIndex], _selectedNames[i]);
            }

            RenderSelection();
        }

        private void UpdateSelection()
        {
            _selectedNames.Clear();
            foreach (object? item in _racerList.CheckedItems)
            {
                string? name = item?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _selectedNames.Add(name);
                }
            }

            RenderSelection();
        }

        private void RenderSelection()
        {
            bool grouped = Scheduling == HeatRaceScheduling.IndependentGroups;
            _laneGrid.Visible = !grouped;
            _selectedGroup.Text = grouped ? "Qualifying Order" : "Rotation Queue";
            _heatLayout.SetRow(_selectedGroup, grouped ? 2 : 3);
            _heatLayout.SetRowSpan(_selectedGroup, grouped ? 2 : 1);
            _laneGrid.Rows.Clear();
            IReadOnlyList<string> laneRacers = FirstHeatLaneRacers;
            for (int lane = 0; lane < _activeLaneCount; lane++)
            {
                string racer = lane < laneRacers.Count ? laneRacers[lane] : "";
                string nextLane = string.IsNullOrWhiteSpace(racer)
                    ? string.Empty
                    : Format == RaceFormat.LapFinish ? string.Empty : GetNextRotationLaneName(lane);
                int rowIndex = _laneGrid.Rows.Add(_laneNames[lane], racer, nextLane);
                DataGridViewCell laneCell = _laneGrid.Rows[rowIndex].Cells[0];
                laneCell.Style.BackColor = _laneColors[lane];
                laneCell.Style.ForeColor = GetContrastingTextColor(_laneColors[lane]);
                laneCell.Style.SelectionBackColor = _laneColors[lane];
                laneCell.Style.SelectionForeColor = GetContrastingTextColor(_laneColors[lane]);
            }

            _selectedRacers.Items.Clear();
            _laneGrid.Columns["next"]!.Visible = Format != RaceFormat.LapFinish;
            if (Format == RaceFormat.LapFinish) _selectedGroup.Text = "Starting Lanes";
            for (int i = 0; i < _selectedNames.Count; i++)
            {
                string prefix = i < _initialLaneIndexes.Count
                    ? _laneNames[_initialLaneIndexes[i]]
                    : "Waiting";
                _selectedRacers.Items.Add(grouped ? $"{i + 1}. {_selectedNames[i]}" : $"{prefix}: {_selectedNames[i]}");
            }

            int waiting = Math.Max(0, _selectedNames.Count - _activeLaneCount);
            string entryLane = _laneNames[_rotationLaneIndexes[0]];
            string rotateOutLane = _laneNames[_rotationLaneIndexes[_rotationLaneIndexes.Count - 1]];
            _queueLabel.Text = Format == RaceFormat.LapFinish
                ? $"{_selectedNames.Count} selected; maximum {_activeLaneCount}. Single race; no rotation."
                : grouped
                ? $"{_selectedNames.Count} selected; {(_selectedNames.Count + _activeLaneCount - 1) / _activeLaneCount} qualifying-ranked group(s)."
                : waiting > 0
                ? $"{_selectedNames.Count} selected; {waiting} waiting. New racers enter on {entryLane} after {rotateOutLane} rotates out."
                : $"{_selectedNames.Count} selected; no racers waiting. Racers cycle from {rotateOutLane} back to {entryLane}.";
            _okButton.Enabled = _selectedNames.Count >= 2 && (Format != RaceFormat.LapFinish || _selectedNames.Count <= _activeLaneCount);
        }

        private void UpdateSchedulingControl()
        {
            _scheduling.Enabled = Format == RaceFormat.HeatRace;
            _scheduling.SelectedIndex = (int)Scheduling;
            bool laps = Format == RaceFormat.LapFinish;
            _lapTarget.Visible = _lapTargetLabel.Visible = laps;
            if (_heatLengthLabel != null)
            {
                _heatLengthSeconds.Visible = _heatLengthLabel.Visible = !laps;
                _betweenHeatsSeconds.Visible = _betweenHeatsLabel.Visible = !laps;
            }
        }

        private string[] GetFirstHeatLaneRacers()
        {
            string[] laneRacers = new string[LapProtocolParser.LaneCount];
            Array.Fill(laneRacers, string.Empty);
            for (int i = 0; i < _initialLaneIndexes.Count; i++)
            {
                int laneIndex = _initialLaneIndexes[i];
                laneRacers[laneIndex] = i < _selectedNames.Count ? _selectedNames[i] : string.Empty;
            }

            return laneRacers;
        }

        private string GetNextRotationLaneName(int laneIndex)
        {
            for (int i = 0; i < _rotationLaneIndexes.Count; i++)
            {
                if (_rotationLaneIndexes[i] != laneIndex)
                {
                    continue;
                }

                if (i < _rotationLaneIndexes.Count - 1)
                {
                    return _laneNames[_rotationLaneIndexes[i + 1]];
                }

                return _selectedNames.Count > _activeLaneCount
                    ? "Rotate out"
                    : _laneNames[_rotationLaneIndexes[0]];
            }

            return "Rotate out";
        }

        private static Color GetContrastingTextColor(Color background)
        {
            double luminance =
                (0.299 * background.R) +
                (0.587 * background.G) +
                (0.114 * background.B);
            return luminance >= 150 ? Color.Black : Color.White;
        }
    }
}
