namespace tlp
{
    public sealed class QualifyingSetup : Form
    {
        private readonly ComboBox _lane = new();
        private readonly NumericUpDown _durationSeconds = new();
        private readonly IReadOnlyList<LaneConfiguration> _lanes;

        public int LaneIndex => _lane.SelectedIndex;
        public int DurationSeconds => (int)_durationSeconds.Value;

        public QualifyingSetup(
            int activeLaneCount,
            IReadOnlyList<LaneConfiguration> laneConfigurations)
        {
            int laneCount = Math.Clamp(activeLaneCount, 2, LapProtocolParser.LaneCount);
            _lanes = laneConfigurations.Take(laneCount).ToArray();
            Text = "Qualifying";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(360, 142);

            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(12)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(layout);

            layout.Controls.Add(CreateLabel("Qualifying lane"), 0, 0);
            _lane.Dock = DockStyle.Fill;
            _lane.DropDownStyle = ComboBoxStyle.DropDownList;
            _lane.DrawMode = DrawMode.OwnerDrawFixed;
            _lane.DrawItem += DrawLane;
            foreach (LaneConfiguration lane in _lanes)
            {
                _lane.Items.Add(lane.Name);
            }
            layout.Controls.Add(_lane, 1, 0);

            layout.Controls.Add(CreateLabel("Time per racer (seconds)"), 0, 1);
            _durationSeconds.Minimum = 5;
            _durationSeconds.Maximum = 3600;
            _durationSeconds.Width = 90;
            layout.Controls.Add(_durationSeconds, 1, 1);

            FlowLayoutPanel buttons = new()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft
            };
            layout.SetColumnSpan(buttons, 2);
            layout.Controls.Add(buttons, 0, 2);

            Button cancel = new()
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true
            };
            buttons.Controls.Add(cancel);
            Button ok = new()
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true
            };
            buttons.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;

            LoadSettings(laneCount);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                AppDatabase.SaveQualifyingSettings(
                    new QualifyingSetupSettings(LaneIndex, DurationSeconds));
            }

            base.OnFormClosing(e);
        }

        private void LoadSettings(int laneCount)
        {
            QualifyingSetupSettings settings = AppDatabase.LoadQualifyingSettings(
                new QualifyingSetupSettings(0, 30));
            _lane.SelectedIndex = Math.Clamp(settings.LaneIndex, 0, laneCount - 1);
            _durationSeconds.Value = Math.Clamp(
                settings.DurationSeconds,
                (int)_durationSeconds.Minimum,
                (int)_durationSeconds.Maximum);
        }

        private void DrawLane(object? sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= _lanes.Count)
            {
                return;
            }

            LaneConfiguration lane = _lanes[e.Index];
            Rectangle swatch = new(e.Bounds.X + 3, e.Bounds.Y + 3, 22, e.Bounds.Height - 6);
            using SolidBrush swatchBrush = new(lane.Color);
            e.Graphics.FillRectangle(swatchBrush, swatch);
            e.Graphics.DrawRectangle(Pens.Black, swatch);
            TextRenderer.DrawText(
                e.Graphics,
                lane.Name,
                e.Font,
                new Rectangle(e.Bounds.X + 31, e.Bounds.Y, e.Bounds.Width - 31, e.Bounds.Height),
                e.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        }

        private static Label CreateLabel(string text) =>
            new()
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft
            };
    }
}
