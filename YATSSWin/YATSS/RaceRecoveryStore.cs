using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace YATSS;

// Small durable deltas between occasional full checkpoints avoid rewriting a
// day's lap history on every crossing. This database is not the settings DB.
public sealed class RaceRecoveryStore : IDisposable
{
    public const int SchemaVersion = 1;
    private readonly FileStream _lease;
    private readonly SqliteConnection _connection;
    private readonly List<HeatRaceLapRecord> _heatLaps = new();
    private readonly List<LaneLapRecord>[] _laneLaps = Enumerable.Range(0, 8).Select(_ => new List<LaneLapRecord>()).ToArray();
    private RaceRecoveryFrame? _metadata;
    private int _sinceCheckpoint;
    public RaceRecoveryFrame? Cursor => _metadata;

    public RaceRecoveryStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, DefaultTimeout = 1
        }.ToString());
        try
        {
            _connection.Open();
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=FULL;
                CREATE TABLE IF NOT EXISTS recovery_version (version INTEGER NOT NULL);
                INSERT INTO recovery_version SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM recovery_version);
                """;
            command.ExecuteNonQuery();
            command.CommandText = "SELECT version FROM recovery_version";
            if (Convert.ToInt32(command.ExecuteScalar()) != SchemaVersion)
                throw new InvalidDataException("Unsupported active-race journal version; preserve the file for recovery.");
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS checkpoint (id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS journal (id INTEGER PRIMARY KEY, payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS archive (id INTEGER PRIMARY KEY, closed_at TEXT NOT NULL, reason TEXT NOT NULL, payload TEXT NOT NULL);
                """;
            command.ExecuteNonQuery();
            // Read checkpoint first, then committed deltas in insertion order.
            command.CommandText = "SELECT payload FROM checkpoint WHERE id=1";
            if (command.ExecuteScalar() is string checkpoint) Apply(Deserialize(checkpoint));
            command.CommandText = "SELECT payload FROM journal ORDER BY id";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) { Apply(Deserialize(reader.GetString(0))); _sinceCheckpoint++; }
        }
        catch
        {
            _connection.Dispose(); _lease.Dispose(); throw;
        }
    }

    private static RaceRecoveryFrame Deserialize(string json) =>
        JsonSerializer.Deserialize<RaceRecoveryFrame>(json) ?? throw new InvalidDataException("Empty race journal record.");

    private void Validate(RaceRecoveryFrame frame)
    {
        if (frame.SchemaVersion != SchemaVersion || frame.EventId == Guid.Empty || frame.Heat == null ||
            frame.Qualifying == null || frame.Options == null || frame.Lanes == null || frame.Lanes.Length != 8 ||
            frame.ConfiguredRacers == null || frame.Qualifying.Racers == null || frame.Qualifying.Results == null ||
            frame.Heat.LaneConfigurations == null || frame.Heat.Qualifiers == null ||
            frame.Heat.Groups == null || frame.Heat.Lanes == null || frame.Heat.Waiting == null ||
            frame.Heat.Seen == null || frame.Heat.Results == null || frame.Heat.Adjustments == null ||
            frame.Heat.Approvals == null || frame.Heat.TieOrder == null ||
            frame.Lanes.Any(lane => lane == null || lane.Snapshot == null || lane.Snapshot.Laps == null) ||
            !frame.Lanes.Select(lane => lane.Snapshot.LaneIndex).Order().SequenceEqual(Enumerable.Range(0, 8)) ||
            (_metadata != null && _metadata.EventId != frame.EventId))
            throw new InvalidDataException("Invalid race journal record.");
        foreach (LaneRecovery lane in frame.Lanes)
        {
            LaneRecovery? prior = _metadata?.Lanes.Single(item => item.Snapshot.LaneIndex == lane.Snapshot.LaneIndex);
            int expected = prior?.Epoch == lane.Epoch ? _laneLaps[lane.Snapshot.LaneIndex].Count : 0;
            if (lane.StartIndex != expected) throw new InvalidDataException("Race journal lap sequence is incomplete.");
        }
    }

    private void Apply(RaceRecoveryFrame frame)
    {
        Validate(frame);
        if (frame.Heat.Laps != null) { _heatLaps.Clear(); _heatLaps.AddRange(frame.Heat.Laps); }
        foreach (LaneRecovery lane in frame.Lanes)
        {
            List<LaneLapRecord> history = _laneLaps[lane.Snapshot.LaneIndex];
            if (lane.StartIndex == 0) history.Clear();
            history.AddRange(lane.Snapshot.Laps);
        }
        _metadata = frame;
    }

    public RaceRecoveryFrame? Read()
    {
        if (_metadata == null) return null;
        return _metadata with
        {
            Heat = _metadata.Heat with { Laps = _heatLaps.ToArray() },
            Lanes = _metadata.Lanes.Select(lane => lane with
            {
                StartIndex = 0,
                Snapshot = lane.Snapshot with { Laps = _laneLaps[lane.Snapshot.LaneIndex].ToArray() }
            }).ToArray()
        };
    }

    public void Save(RaceRecoveryFrame frame)
    {
        Validate(frame);
        using SqliteTransaction transaction = _connection.BeginTransaction();
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO journal(payload) VALUES ($payload)";
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(frame));
        command.ExecuteNonQuery();
        transaction.Commit();
        Apply(frame);
        // Compaction is maintenance after the durable event, not a prerequisite
        // for acknowledging that event. Failure still stops racing at the caller.
        if (++_sinceCheckpoint >= 256) Checkpoint();
    }

    private void Checkpoint()
    {
        using SqliteTransaction transaction = _connection.BeginTransaction();
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO checkpoint(id,payload) VALUES(1,$payload); DELETE FROM journal;";
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(Read()));
        command.ExecuteNonQuery();
        transaction.Commit();
        _sinceCheckpoint = 0;
    }

    public void Archive(string reason)
    {
        if (_metadata == null) return;
        using SqliteTransaction transaction = _connection.BeginTransaction();
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO archive(closed_at,reason,payload) VALUES($time,$reason,$payload);
            DELETE FROM journal; DELETE FROM checkpoint;
            """;
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(Read()));
        command.ExecuteNonQuery();
        transaction.Commit();
        _metadata = null; _sinceCheckpoint = 0; _heatLaps.Clear();
        foreach (var laps in _laneLaps) laps.Clear();
    }

    public void Dispose() { _connection.Dispose(); _lease.Dispose(); }
}
