namespace tlp
{
    public sealed class QualifyingLaneSelection : Form
    {
        private readonly IReadOnlyList<QualifyingResult> _rankedResults;
        private readonly IReadOnlyList<LaneConfiguration> _lanes;
        private readonly Label _prompt = new();
        private readonly FlowLayoutPanel _laneButtons = new();
        private readonly int[] _selectedLaneByRank;
        private int _currentRank;

        public IReadOnlyList<string> SeededRacers { get; private set; } = Array.Empty<string>();

        public QualifyingLaneSelection(
            IReadOnlyList<QualifyingResult> rankedResults,
            int activeLaneCount,
            IReadOnlyList<LaneConfiguration> laneConfigurations)
        {
            _rankedResults = rankedResults;
            int laneCount = Math.Clamp(activeLaneCount, 2, LapProtocolParser.LaneCount);
            _lanes = laneConfigurations.Take(laneCount).ToArray();
            _selectedLaneByRank = Enumerable.Repeat(-1, Math.Min(laneCount, rankedResults.Count)).ToArray();

            Text = "Choose Starting Lanes";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ClientSize = new Size(640, 440);

            TableLayoutPanel root = new()
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            _prompt.Dock = DockStyle.Fill;
            _prompt.Font = new Font(Font, FontStyle.Bold);
            _prompt.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_prompt, 0, 0);

            _laneButtons.Dock = DockStyle.Fill;
            _laneButtons.FlowDirection = FlowDirection.LeftToRight;
            _laneButtons.WrapContents = true;
            root.Controls.Add(_laneButtons, 0, 1);

            ListBox ranking = new()
            {
                Dock = DockStyle.Fill
            };
            for (int i = 0; i < rankedResults.Count; i++)
            {
                QualifyingResult result = rankedResults[i];
                string time = result.BestLapMilliseconds.HasValue
                    ? $"{result.BestLapMilliseconds.Value / 1000.0:0.000}s"
                    : "No valid lap";
                ranking.Items.Add($"{i + 1}. {result.RacerName} - {time}");
            }
            root.Controls.Add(ranking, 0, 2);

            BuildLaneButtons();
            ShowCurrentChooser();
        }

        private void BuildLaneButtons()
        {
            for (int lane = 0; lane < _lanes.Count; lane++)
            {
                LaneConfiguration laneConfiguration = _lanes[lane];
                Button button = new()
                {
                    Text = laneConfiguration.Name,
                    BackColor = laneConfiguration.Color,
                    ForeColor = GetContrastingTextColor(laneConfiguration.Color),
                    UseVisualStyleBackColor = false,
                    Width = 120,
                    Height = 42,
                    Tag = lane
                };
                button.Click += ChooseLane;
                _laneButtons.Controls.Add(button);
            }
        }

        private void ChooseLane(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: int lane } button ||
                _currentRank >= _selectedLaneByRank.Length)
            {
                return;
            }

            _selectedLaneByRank[_currentRank] = lane;
            button.Enabled = false;
            _currentRank++;
            if (_currentRank < _selectedLaneByRank.Length)
            {
                ShowCurrentChooser();
                return;
            }

            BuildSeededRacers();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowCurrentChooser()
        {
            _prompt.Text = _selectedLaneByRank.Length == 0
                ? "No lane choices are required."
                : $"{_rankedResults[_currentRank].RacerName} chooses a starting lane";
        }

        private void BuildSeededRacers()
        {
            SeededRacers = QualifyingController.BuildSeededRacers(
                _rankedResults,
                _selectedLaneByRank,
                _lanes.Count);
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
