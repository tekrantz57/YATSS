using System.Diagnostics;

namespace YATSS
{
    public sealed class SerialLog
    {
        private readonly object _gate = new();
        private readonly string _path;

        public static string CurrentPath
        {
            get
            {
                string logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "YATSS",
                    "logs");
                return Path.Combine(logDirectory, $"serial-{DateTime.Now:yyyyMMdd}.log");
            }
        }

        public SerialLog()
        {
            string logDirectory = Path.GetDirectoryName(CurrentPath) ?? string.Empty;
            Directory.CreateDirectory(logDirectory);
            _path = CurrentPath;
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
