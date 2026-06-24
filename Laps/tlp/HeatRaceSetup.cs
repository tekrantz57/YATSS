using Microsoft.Data.Sqlite;

namespace tlp
{
    public sealed class HeatRaceSetup : Form
    {
        private static readonly string[] LaneNames =
        {
            "Red",
            "Green",
            "Blue",
            "Purple",
            "Black",
            "Yellow",
            "Orange",
            "White"
        };

        private readonly CheckedListBox _racerList = new();
        private readonly ListBox _selectedRacers = new();
        private readonly DataGridView _laneGrid = new();
        private readonly Label _queueLabel = new();
        private readonly NumericUpDown _heatLengthMinutes = new();
        private readonly NumericUpDown _betweenHeatsSeconds = new();
        private readonly Random _random = new();
        private readonly List<string> _selectedNames = new();

        public int HeatLengthMinutes => (int)_heatLengthMinutes.Value;
        public int BetweenHeatsSeconds => (int)_betweenHeatsSeconds.Value;
        public IReadOnlyList<string> SelectedRacers => _selectedNames.ToArray();
        public IReadOnlyList<string> FirstHeatLaneRacers => GetFirstHeatLaneRacers();

        public HeatRaceSetup()
        {
            Text = "Heat Race";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(860, 560);

            BuildLayout();
            LoadRacers();
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
            racerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            racerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
            racersGroup.Controls.Add(racerLayout);

            _racerList.Dock = DockStyle.Fill;
            _racerList.CheckOnClick = true;
            _racerList.ItemCheck += (_, _) => BeginInvoke(UpdateSelection);
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

            _queueLabel.Dock = DockStyle.Fill;
            _queueLabel.TextAlign = ContentAlignment.MiddleLeft;
            racerLayout.Controls.Add(_queueLabel, 0, 2);

            GroupBox heatGroup = new()
            {
                Text = "First Heat",
                Dock = DockStyle.Fill
            };
            root.Controls.Add(heatGroup, 1, 0);

            TableLayoutPanel heatLayout = new()
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                Padding = new Padding(8)
            };
            heatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            heatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 70F));
            heatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30F));
            heatGroup.Controls.Add(heatLayout);

            FlowLayoutPanel heatSettings = new()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            heatLayout.Controls.Add(heatSettings, 0, 0);

            Label heatLengthLabel = new()
            {
                Text = "Heat length (minutes)",
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            };
            heatSettings.Controls.Add(heatLengthLabel);

            _heatLengthMinutes.Minimum = 1;
            _heatLengthMinutes.Maximum = 60;
            _heatLengthMinutes.Value = 3;
            _heatLengthMinutes.Width = 64;
            heatSettings.Controls.Add(_heatLengthMinutes);

            Label betweenHeatsLabel = new()
            {
                Text = "Between heats (seconds)",
                AutoSize = true,
                Margin = new Padding(18, 6, 8, 0)
            };
            heatSettings.Controls.Add(betweenHeatsLabel);

            _betweenHeatsSeconds.Minimum = 0;
            _betweenHeatsSeconds.Maximum = 300;
            _betweenHeatsSeconds.Value = 0;
            _betweenHeatsSeconds.Width = 64;
            heatSettings.Controls.Add(_betweenHeatsSeconds);

            ConfigureLaneGrid();
            heatLayout.Controls.Add(_laneGrid, 0, 1);

            GroupBox selectedGroup = new()
            {
                Text = "Rotation Queue",
                Dock = DockStyle.Fill
            };
            heatLayout.Controls.Add(selectedGroup, 0, 2);

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

            Button okButton = new()
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true
            };
            buttons.Controls.Add(okButton);

            Button randomizeButton = new()
            {
                Text = "Randomize",
                AutoSize = true
            };
            randomizeButton.Click += (_, _) => RandomizeSelectedRacers();
            buttons.Controls.Add(randomizeButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
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
            foreach (string name in LoadRacerNames())
            {
                _racerList.Items.Add(name, false);
            }
        }

        private static List<string> LoadRacerNames()
        {
            List<string> racerNames = new();
            using SqliteCommand command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users ORDER BY name";

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    string name = reader.GetString(0).Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        racerNames.Add(name);
                    }
                }
            }

            return racerNames;
        }

        private void SetAllRacersChecked(bool isChecked)
        {
            for (int i = 0; i < _racerList.Items.Count; i++)
            {
                _racerList.SetItemChecked(i, isChecked);
            }

            UpdateSelection();
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
            _laneGrid.Rows.Clear();
            for (int lane = 0; lane < LaneNames.Length; lane++)
            {
                string racer = lane < _selectedNames.Count ? _selectedNames[lane] : "";
                string nextLane = lane == LaneNames.Length - 1 ? "Rotate out" : LaneNames[lane + 1];
                _laneGrid.Rows.Add(LaneNames[lane], racer, nextLane);
            }

            _selectedRacers.Items.Clear();
            for (int i = 0; i < _selectedNames.Count; i++)
            {
                string prefix = i < LaneNames.Length ? LaneNames[i] : "Waiting";
                _selectedRacers.Items.Add($"{prefix}: {_selectedNames[i]}");
            }

            int waiting = Math.Max(0, _selectedNames.Count - LaneNames.Length);
            _queueLabel.Text = $"{_selectedNames.Count} selected; {waiting} waiting. New racers enter on Red after White rotates out.";
        }

        private string[] GetFirstHeatLaneRacers()
        {
            string[] laneRacers = new string[LaneNames.Length];
            for (int i = 0; i < HeatRaceController.RotationLaneIndexes.Count; i++)
            {
                int laneIndex = HeatRaceController.RotationLaneIndexes[i];
                laneRacers[laneIndex] = i < _selectedNames.Count ? _selectedNames[i] : string.Empty;
            }

            return laneRacers;
        }
    }
}
