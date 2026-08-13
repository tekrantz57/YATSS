namespace YATSS
{
    public sealed class SerialLogTailForm : Form
    {
        private const int MaxCharacters = 60000;
        private const int EmGetFirstVisibleLine = 0x00CE;
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
            Controls.Add(_logTextBox);

            _refreshTimer.Interval = 1000;
            _refreshTimer.Tick += (_, _) => RefreshLog();
            Shown += (_, _) =>
            {
                RefreshLog(force: true);
                BeginInvoke(ScrollToEnd);
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

            if (!force && !IsScrolledToEnd())
            {
                Text = "Serial Log Tail (paused)";
                return;
            }

            FileInfo fileInfo = new(path);
            if (!force && fileInfo.Length == _lastLength)
            {
                Text = "Serial Log Tail";
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
            ScrollToEnd();
            Text = "Serial Log Tail";
        }

        private void ScrollToEnd()
        {
            if (IsDisposed || _logTextBox.IsDisposed)
            {
                return;
            }

            _logTextBox.SelectionStart = _logTextBox.TextLength;
            _logTextBox.SelectionLength = 0;
            _logTextBox.ScrollToCaret();
        }

        private bool IsScrolledToEnd()
        {
            if (_logTextBox.TextLength == 0)
            {
                return true;
            }

            int firstVisibleLine = unchecked((int)SendMessage(
                _logTextBox.Handle,
                EmGetFirstVisibleLine,
                IntPtr.Zero,
                IntPtr.Zero));
            if (firstVisibleLine < 0)
            {
                return true;
            }

            int visibleLines = Math.Max(1, _logTextBox.ClientSize.Height / _logTextBox.Font.Height);
            int lastLine = _logTextBox.GetLineFromCharIndex(_logTextBox.TextLength);
            return firstVisibleLine + visibleLines + 1 >= lastLine;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
