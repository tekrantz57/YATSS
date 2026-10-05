namespace YATSS
{
    internal sealed class DistanceApprovalForm : Form
    {
        private readonly NumericUpDown _partial = new() { Minimum = 0, Maximum = 99, Width = 90 };
        private readonly TextBox _reason = new() { Dock = DockStyle.Fill, Text = "Confirmed track position" };
        public int PartialHundredths => (int)_partial.Value;
        public string Reason => _reason.Text.Trim();

        public DistanceApprovalForm(string racer, int laps, int? estimate, string stage)
        {
            Text = $"{stage} Distance - {racer}";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ControlBox = false;
            ClientSize = new Size(500, 250);
            TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 5, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            foreach (int height in new[] { 40, 40, 40, 50, 50 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.Controls.Add(new Label { Text = $"Completed laps: {laps}", AutoSize = true }, 0, 0);
            root.Controls.Add(new Label { Text = estimate.HasValue ? $"Estimated fraction: {estimate / 100m:0.00}" : "Estimated fraction: unknown", AutoSize = true }, 0, 1);
            root.SetColumnSpan(root.GetControlFromPosition(0, 1)!, 2);
            root.Controls.Add(new Label { Text = "Approved fraction (hundredths)", AutoSize = true }, 0, 2);
            root.Controls.Add(_partial, 1, 2);
            _partial.Value = estimate ?? 0;
            root.Controls.Add(_reason, 0, 3);
            root.SetColumnSpan(_reason, 2);
            CheckBox confirmed = new() { Text = "Track position confirmed", AutoSize = true };
            root.Controls.Add(confirmed, 0, 4);
            Button approve = new() { Text = "Confirm", Enabled = false, AutoSize = true };
            void Validate(object? sender, EventArgs args) => approve.Enabled = confirmed.Checked && Reason.Length > 0;
            confirmed.CheckedChanged += Validate;
            _reason.TextChanged += Validate;
            approve.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            root.Controls.Add(approve, 1, 4);
            Controls.Add(root);
            AcceptButton = approve;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && DialogResult != DialogResult.OK) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }

    internal sealed class DistanceTieForm : Form
    {
        private readonly List<DistanceStanding> _rows;
        private readonly ListBox _list = new() { Dock = DockStyle.Fill };
        public IReadOnlyList<int> RacerOrder => _rows.Select(row => row.RacerId).ToArray();
        public static bool HasExactTies(IEnumerable<DistanceStanding> rows) =>
            rows.GroupBy(row => (row.CombinedHundredths, row.BestLapMilliseconds)).Any(group => group.Count() > 1);

        public DistanceTieForm(IReadOnlyList<DistanceStanding> rows, string stage)
        {
            _rows = rows.ToList();
            Text = $"{stage} - Resolve Exact Ties";
            StartPosition = FormStartPosition.CenterParent;
            ControlBox = false;
            Size = new Size(620, 420);
            MinimumSize = new Size(550, 320);
            TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.Controls.Add(_list, 0, 0);
            FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill };
            foreach ((string text, int delta) in new[] { ("Move Up", -1), ("Move Down", 1) })
            {
                Button move = new() { Text = text, AutoSize = true };
                move.Click += (_, _) =>
                {
                    int index = _list.SelectedIndex, next = index + delta;
                    if (index < 0 || next < 0 || next >= _rows.Count) return;
                    if ((_rows[index].CombinedHundredths, _rows[index].BestLapMilliseconds) !=
                        (_rows[next].CombinedHundredths, _rows[next].BestLapMilliseconds)) return;
                    (_rows[index], _rows[next]) = (_rows[next], _rows[index]);
                    Render();
                    _list.SelectedIndex = next;
                };
                buttons.Controls.Add(move);
            }
            Button confirm = new() { Text = "Confirm Order", AutoSize = true };
            confirm.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            buttons.Controls.Add(confirm);
            root.Controls.Add(buttons, 0, 1);
            Controls.Add(root);
            Render();
        }

        private void Render()
        {
            _list.Items.Clear();
            foreach (DistanceStanding row in _rows)
                _list.Items.Add($"{row.RacerName} - {DistanceScoring.Format(row.CombinedHundredths)} - best {(row.BestLapMilliseconds.HasValue ? $"{row.BestLapMilliseconds / 1000d:0.000}s" : "none")}");
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && DialogResult != DialogResult.OK) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }

    internal sealed class LiveStandingsForm : Form
    {
        private readonly DataGridView _grid = new()
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        public LiveStandingsForm()
        {
            Text = "Live Standings - Combined Distance";
            Size = new Size(1000, 500);
            MinimumSize = new Size(720, 300);
            foreach (string column in new[] { "Position", "Racer", "Group", "Lane", "Qualifying", "Race Distance", "Combined Distance", "Projected Final Distance" })
                _grid.Columns.Add(column, column);
            foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.Columns[0].FillWeight = 50;
            _grid.Columns[2].FillWeight = 50;
            _grid.Columns[3].FillWeight = 70;
            _grid.Columns[1].FillWeight = 160;
            _grid.Columns[7].FillWeight = 160;
            _grid.Columns[7].DefaultCellStyle.ForeColor = SystemColors.GrayText;
            Controls.Add(_grid);
        }
        public void UpdateStandings(IReadOnlyList<DistanceStanding> rows)
        {
            while (_grid.Rows.Count > rows.Count) _grid.Rows.RemoveAt(_grid.Rows.Count - 1);
            while (_grid.Rows.Count < rows.Count) _grid.Rows.Add();
            for (int index = 0; index < rows.Count; index++)
            {
                DistanceStanding row = rows[index];
                object[] values = new object[] { index + 1, row.RacerName, row.Group, row.Lane,
                    DistanceScoring.Format(row.QualifyingHundredths),
                    DistanceScoring.Format(row.RaceLaps * 100L + (row.FinalPartialHundredths ?? 0)),
                    DistanceScoring.Format(row.CombinedHundredths),
                    row.ProjectedHundredths.HasValue ? DistanceScoring.Format(row.ProjectedHundredths.Value) : "--" };
                for (int column = 0; column < values.Length; column++)
                    if (!Equals(_grid.Rows[index].Cells[column].Value, values[column])) _grid.Rows[index].Cells[column].Value = values[column];
            }
        }
    }
}
