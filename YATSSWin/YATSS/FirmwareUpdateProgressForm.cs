namespace YATSS
{
    public sealed class FirmwareUpdateProgressForm : Form
    {
        private readonly Label _statusLabel = new();
        private readonly TextBox _outputTextBox = new();
        private readonly ProgressBar _progressBar = new();
        private bool _operationComplete;

        public FirmwareUpdateProgressForm()
        {
            Text = "Controller Firmware Update";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 430);
            MinimumSize = new Size(640, 360);
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel layout = new()
            {
                ColumnCount = 1,
                RowCount = 3,
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _statusLabel.AutoSize = false;
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            _statusLabel.Text = "Preparing controller firmware update...";
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            _progressBar.Dock = DockStyle.Fill;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 25;

            _outputTextBox.Dock = DockStyle.Fill;
            _outputTextBox.Multiline = true;
            _outputTextBox.ReadOnly = true;
            _outputTextBox.ScrollBars = ScrollBars.Vertical;
            _outputTextBox.Font = new Font("Consolas", 9F);
            _outputTextBox.BackColor = SystemColors.Window;

            layout.Controls.Add(_statusLabel, 0, 0);
            layout.Controls.Add(_progressBar, 0, 1);
            layout.Controls.Add(_outputTextBox, 0, 2);
            Controls.Add(layout);

            FormClosing += (_, args) =>
            {
                if (!_operationComplete)
                {
                    args.Cancel = true;
                }
            };
        }

        public IProgress<string> CreateProgress() => new Progress<string>(AppendOutput);

        public void SetStatus(string status)
        {
            _statusLabel.Text = status;
        }

        public void Complete()
        {
            _operationComplete = true;
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.Value = 100;
        }

        private void AppendOutput(string line)
        {
            _outputTextBox.AppendText(line + Environment.NewLine);
            _outputTextBox.SelectionStart = _outputTextBox.TextLength;
            _outputTextBox.ScrollToCaret();
        }
    }
}
