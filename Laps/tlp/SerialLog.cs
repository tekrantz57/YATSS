using System.Diagnostics;

namespace tlp
{
    public sealed class SerialLog
    {
        private readonly object _gate = new();
        private readonly string _path;

        public SerialLog()
        {
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "tlp",
                "logs");
            Directory.CreateDirectory(logDirectory);
            _path = Path.Combine(logDirectory, $"serial-{DateTime.Now:yyyyMMdd}.log");
        }

        public void Info(string message) => Write("INFO", message);

        public void Raw(string line) => Write("RAW", line);

        public void Warn(string message) => Write("WARN", message);

        public void Error(Exception exception, string message) =>
            Write("ERROR", $"{message}: {exception.GetType().Name}: {exception.Message}");

        private void Write(string level, string message)
        {
            string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
            Trace.WriteLine(line);
            lock (_gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
    }
}
