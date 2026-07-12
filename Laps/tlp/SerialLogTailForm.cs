namespace tlp
{
    public sealed class SerialLogTailForm : Form
    {
        private const int MaxCharacters = 60000;
        private readonly TextBox _logTextBox = new();
        private readonly System.Windows.Forms.Timer _refreshTimer = new();
        private long _lastLength = -1;

        public SerialLogTailForm()
        {
            Text = "Serial Log Tail";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(950, 520);

            _logTextBox.Dock = DockStyle.Fill;
            _logTextBox.Multiline = true;
            _logTextBox.ReadOnly = true;
            _logTextBox.ScrollBars = ScrollBars.Both;
            _logTextBox.WordWrap = false;
            _logTextBox.Font = new Font(FontFamily.GenericMonospace, 9F);
            Controls.Add(_logTextBox);

            _refreshTimer.Interval = 1000;
            _refreshTimer.Tick += (_, _) => RefreshLog();
            Shown += (_, _) =>
            {
                RefreshLog(force: true);
                _refreshTimer.Start();
            };
            FormClosed += (_, _) => _refreshTimer.Stop();
        }

        private void RefreshLog(bool force = false)
        {
            string path = SerialLog.CurrentPath;
            if (!File.Exists(path))
            {
                _logTextBox.Text = $"Waiting for log file:{Environment.NewLine}{path}";
                _lastLength = -1;
                return;
            }

            FileInfo fileInfo = new(path);
            if (!force && fileInfo.Length == _lastLength)
            {
                return;
            }

            _lastLength = fileInfo.Length;
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > MaxCharacters)
            {
                stream.Seek(-MaxCharacters, SeekOrigin.End);
            }

            using StreamReader reader = new(stream);
            string text = reader.ReadToEnd();
            int firstFullLine = text.IndexOf(Environment.NewLine, StringComparison.Ordinal);
            if (stream.Position >= MaxCharacters && firstFullLine >= 0)
            {
                text = text[(firstFullLine + Environment.NewLine.Length)..];
            }

            _logTextBox.Text = text;
            _logTextBox.SelectionStart = _logTextBox.TextLength;
            _logTextBox.ScrollToCaret();
        }
    }
}
