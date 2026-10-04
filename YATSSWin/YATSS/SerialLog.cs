using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;

namespace YATSS
{
    public sealed class SerialLog : IDisposable
    {
        internal const int QueueCapacity = 1024;
        internal const int RetentionDays = 30;
        private const int MaxEntryCharacters = 8192;
        private readonly Channel<Entry> _entries = Channel.CreateBounded<Entry>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        private readonly string _directory;
        private readonly Func<DateTimeOffset> _now;
        private readonly Action<string, string> _append;
        private readonly Action<string> _trace;
        private readonly Task _writer;
        private string? _failure;
        private long _dropped;
        private DateTimeOffset _retryAfter;
        private DateOnly? _retentionDate;

        public static string CurrentPath => GetPath(DefaultDirectory, DateTimeOffset.Now);
        private static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YATSS", "logs");
        public string? Failure => Volatile.Read(ref _failure);
        public long DroppedEntries => Interlocked.Read(ref _dropped);
        public event Action<string>? Warning;

        public SerialLog() : this(DefaultDirectory, () => DateTimeOffset.Now) { }

        internal SerialLog(string directory, Func<DateTimeOffset> now,
            Action<string, string>? append = null, Action<string>? trace = null)
        {
            _directory = directory;
            _now = now;
            _append = append ?? File.AppendAllText;
            _trace = trace ?? (line => Trace.WriteLine(line));
            _writer = Task.Run(WriteLoopAsync);
        }

        public void Info(string message) => Write("INFO", message);
        public void Raw(string line) => Write("RAW", line);
        public void Warn(string message) => Write("WARN", message);
        public void Error(Exception exception, string message) =>
            Write("ERROR", $"{message}: {exception.GetType().Name}: {exception.Message}");

        private void Write(string level, string message)
        {
            DateTimeOffset timestamp = _now();
            if (message.Length > MaxEntryCharacters)
            {
                message = message[..MaxEntryCharacters] + " [truncated]";
            }
            string line = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
            if (!_entries.Writer.TryWrite(new Entry(timestamp, line)))
            {
                Interlocked.Increment(ref _dropped);
            }
        }

        internal async Task FlushAsync()
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await _entries.Writer.WriteAsync(new Entry(_now(), null, completion));
            await completion.Task;
        }

        private async Task WriteLoopAsync()
        {
            await foreach (Entry first in _entries.Reader.ReadAllAsync())
            {
                List<Entry> batch = new(64) { first };
                while (batch.Count < 64 && _entries.Reader.TryRead(out Entry? next))
                {
                    batch.Add(next);
                }
                foreach (IGrouping<string, Entry> group in batch.Where(entry => entry.Line != null)
                    .GroupBy(entry => GetPath(_directory, entry.Timestamp)))
                {
                    foreach (Entry entry in group)
                    {
                        try { _trace(entry.Line!); }
                        catch (Exception) { /* A diagnostic listener must not stop race operations. */ }
                    }
                    if (_now() < _retryAfter)
                    {
                        Interlocked.Add(ref _dropped, group.Count());
                        continue;
                    }
                    try
                    {
                        Directory.CreateDirectory(_directory);
                        _append(group.Key, string.Join(Environment.NewLine, group.Select(entry => entry.Line)) + Environment.NewLine);
                        Volatile.Write(ref _failure, null);
                    }
                    catch (Exception ex) when (IsStorageError(ex))
                    {
                        Interlocked.Add(ref _dropped, group.Count());
                        _retryAfter = _now().AddSeconds(30);
                        ReportFailure($"Serial logging unavailable: {ex.Message}. Racing continues; diagnostic entries may be lost.");
                    }
                    if (_now() >= _retryAfter)
                    {
                        try { PruneOldLogs(); }
                        catch (Exception ex) when (IsStorageError(ex))
                        {
                            ReportFailure($"Serial log cleanup unavailable: {ex.Message}. Racing continues.");
                        }
                    }
                }
                foreach (Entry entry in batch)
                {
                    entry.Completion?.TrySetResult();
                }
            }
        }

        private void PruneOldLogs()
        {
            DateOnly today = DateOnly.FromDateTime(_now().DateTime);
            if (_retentionDate == today) return;
            _retentionDate = today;
            foreach (string path in Directory.EnumerateFiles(_directory, "serial-????????.log"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.Length == 15 && name.StartsWith("serial-", StringComparison.Ordinal) &&
                    DateOnly.TryParseExact(name[7..], "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly date) && date < today.AddDays(-(RetentionDays - 1)))
                {
                    File.Delete(path);
                }
            }
        }

        private void ReportFailure(string message)
        {
            string? previous = Interlocked.Exchange(ref _failure, message);
            if (previous == message) return;
            try { Warning?.Invoke(message); }
            catch (Exception) { /* Reporting a logging failure must not depend on logging or UI availability. */ }
        }

        private static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or ArgumentException or NotSupportedException;
        private static string GetPath(string directory, DateTimeOffset timestamp) =>
            Path.Combine(directory, $"serial-{timestamp:yyyyMMdd}.log");

        public void Dispose()
        {
            _entries.Writer.TryComplete();
            // Closing the UI must not wait indefinitely for a stalled disk or trace listener.
            _writer.Wait(TimeSpan.FromSeconds(2));
        }

        private sealed record Entry(DateTimeOffset Timestamp, string? Line, TaskCompletionSource? Completion = null);
    }
}
