namespace YATSS;

internal sealed class HeatRerunChoiceForm : Form
{
    private readonly TextBox _reason = new() { Dock = DockStyle.Fill, Multiline = true, MaxLength = 1000 };
    public string Reason => _reason.Text.Trim();

    public HeatRerunChoiceForm()
    {
        Text = "Rerun Current Heat";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(580, 300);
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Discard the current heat's laps and corrections and restore its starting totals?\r\n\r\nPrevious heats, qualifying credit and lane assignments stay unchanged. Power stays off. Restore cars to their exact heat-start positions before starting again." }, 0, 0);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Reason (required):" }, 0, 1);
        layout.Controls.Add(_reason, 0, 2);
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button confirm = new() { Text = "Discard and Rerun", AutoSize = true, Enabled = false, DialogResult = DialogResult.OK };
        _reason.TextChanged += (_, _) => confirm.Enabled = Reason.Length > 0;
        buttons.Controls.Add(cancel); buttons.Controls.Add(confirm);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        CancelButton = cancel;
        Shown += (_, _) => cancel.Focus();
    }
}
