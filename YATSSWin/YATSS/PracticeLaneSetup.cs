namespace YATSS;

internal sealed class PracticeLaneSetup : Form
{
    private readonly CheckedListBox _lanes = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    internal byte SelectedMask => (byte)Enumerable.Range(0, _lanes.Items.Count)
        .Where(index => _lanes.GetItemChecked(index)).Sum(index => 1 << index);

    internal PracticeLaneSetup(IReadOnlyList<LaneConfiguration> lanes, int activeLaneCount, byte mask)
    {
        Text = "Practice Sensor Lanes";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new(360, 340); Size = new(420, 400);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(12), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        for (int index = 0; index < activeLaneCount; index++)
            _lanes.Items.Add($"{index + 1}. {lanes[index].Name}", (mask & (1 << index)) != 0);
        layout.Controls.Add(_lanes, 0, 0);
        var selection = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var all = new Button { Text = "Select All", AutoSize = true };
        var none = new Button { Text = "Clear", AutoSize = true };
        all.Click += (_, _) => SelectAll(true); none.Click += (_, _) => SelectAll(false);
        selection.Controls.AddRange(new Control[] { all, none }); layout.Controls.Add(selection, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var apply = new Button { Text = "Apply", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { cancel, apply }); layout.Controls.Add(buttons, 0, 2);
        AcceptButton = apply; CancelButton = cancel; Controls.Add(layout);
    }

    private void SelectAll(bool value)
    {
        for (int index = 0; index < _lanes.Items.Count; index++) _lanes.SetItemChecked(index, value);
    }
}
