using System.Globalization;
using Microsoft.Data.Sqlite;

namespace YATSS;

public sealed record RaceArchiveEntry(long Id, string ClosedAt, string Reason, Guid? EventId,
    string RaceName, string Format, string Status, int Heat, int TotalHeats, bool Demo)
{
    public string ArchivedLocal => DateTimeOffset.TryParse(ClosedAt, out var date) ? date.LocalDateTime.ToString("g") : ClosedAt;
}
public sealed record RaceArchivePage(IReadOnlyList<RaceArchiveEntry> Entries, long Count);

public sealed partial class RaceRecoveryStore
{
    private const string ArchiveEventId = "CASE WHEN json_valid(payload) THEN json_extract(payload,'$.EventId') END";

    public RaceArchivePage ListArchives(string search = "", int page = 0, int pageSize = 50)
    {
        if (page < 0 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        using SqliteCommand command = _connection.CreateCommand();
        const string name = "CASE WHEN json_valid(payload) THEN json_extract(payload,'$.Heat.RaceName') END";
        string filter = $"($search='' OR instr(lower(COALESCE({name},'')),lower($search))>0 OR instr(lower(reason),lower($search))>0)";
        command.CommandText = "SELECT COUNT(*) FROM archive WHERE " + filter;
        command.Parameters.AddWithValue("$search", search.Trim());
        long count = Convert.ToInt64(command.ExecuteScalar());
        command.CommandText = $"""
            SELECT id,closed_at,reason,{ArchiveEventId},{name},
              CASE WHEN json_valid(payload) THEN json_extract(payload,'$.Heat.Format') END,
              CASE WHEN json_valid(payload) THEN json_extract(payload,'$.Heat.State') END,
              CASE WHEN json_valid(payload) THEN json_extract(payload,'$.Heat.HeatNumber') END,
              CASE WHEN json_valid(payload) THEN json_extract(payload,'$.Heat.TotalHeats') END,
              CASE WHEN json_valid(payload) THEN json_extract(payload,'$.ReportWritten') END,
              CASE WHEN json_valid(payload) THEN json_extract(payload,'$.Demo') END
            FROM archive WHERE {filter} ORDER BY id DESC LIMIT $limit OFFSET $offset
            """;
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", checked((long)page * pageSize));
        List<RaceArchiveEntry> entries = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string Value(int index) => reader.IsDBNull(index) ? "" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture)!;
            int Number(int index) => int.TryParse(Value(index), out int value) ? value : 0;
            Guid? id = Guid.TryParse(Value(3), out Guid guid) && guid != Guid.Empty ? guid : null;
            bool complete = Number(6) == (int)HeatRaceState.Complete && Number(7) == Number(8) && Number(8) > 0;
            entries.Add(new(reader.GetInt64(0), Value(1), Value(2), id,
                string.IsNullOrWhiteSpace(Value(4)) ? "(Unnamed event)" : Value(4),
                Number(5) switch { (int)RaceFormat.HeatRace => "Heat Race", (int)RaceFormat.CombinedDistance => "Combined Distance", (int)RaceFormat.LapFinish => "Lap Finish", _ => "Unknown" },
                id == null ? "Unreadable" : complete ? Number(9) == 1 ? "Completed / exported" : "Completed / not exported" : "Unfinished",
                Number(7), Number(8), Number(10) == 1));
        }
        return new(entries, count);
    }

    public RaceRecoveryFrame ReadArchive(RaceArchiveEntry entry)
    {
        if (entry.EventId == null) throw new InvalidDataException("Archive identity is unreadable. Preserve this record for diagnosis.");
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM archive WHERE id=$id AND closed_at=$time AND {ArchiveEventId}=$event";
        command.Parameters.AddWithValue("$id", entry.Id); command.Parameters.AddWithValue("$time", entry.ClosedAt);
        command.Parameters.AddWithValue("$event", entry.EventId.Value.ToString());
        if (command.ExecuteScalar() is not string json) throw new InvalidOperationException("Archive changed or was removed. Refresh the list.");
        RaceRecoveryFrame frame = Deserialize(json);
        if (frame.SchemaVersion != SchemaVersion || frame.EventId != entry.EventId || frame.Heat?.Laps == null || frame.Lanes?.Length != 8 ||
            frame.Qualifying?.Results == null || frame.Qualifying.Results.Any(result => result == null) || frame.Qualifying.Racers == null || frame.Heat.Lanes?.Length != 8 ||
            frame.Heat.Qualifiers == null || frame.Heat.Reruns == null || frame.Heat.Results == null || frame.Heat.Adjustments == null ||
            frame.Heat.Groups == null || frame.Heat.Groups.Any(group => group == null) ||
            frame.Heat.Reruns.Any(item => item == null || item.Laps == null || item.LaneResults == null || item.ManualAdjustments == null) ||
            frame.Lanes.Any(lane => lane?.Snapshot?.Laps == null) ||
            !frame.Lanes.Select(lane => lane.Snapshot.LaneIndex).Order().SequenceEqual(Enumerable.Range(0, 8)))
            throw new InvalidDataException("Unsupported or incomplete archive. Preserve it for diagnosis.");
        try { HeatRaceController validation = new(); validation.RestoreRecovery(frame.Heat); }
        catch (Exception ex) when (ex is NullReferenceException or IndexOutOfRangeException)
        { throw new InvalidDataException("Incomplete archived scoring data. Preserve it for diagnosis.", ex); }
        return frame;
    }

    public IReadOnlyList<RaceArchiveEntry> ArchivesOlderThan(DateTimeOffset cutoff)
    {
        // Discover deletion candidates separately; the UI must confirm before deleting.
        List<RaceArchiveEntry> entries = new();
        for (int page = 0; ; page++)
        {
            RaceArchivePage result = ListArchives(page: page, pageSize: 100);
            entries.AddRange(result.Entries.Where(entry => entry.EventId != null &&
                DateTimeOffset.TryParse(entry.ClosedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) && time < cutoff));
            if ((page + 1L) * 100 >= result.Count) break;
        }
        return entries;
    }

    public int DeleteArchives(IReadOnlyList<RaceArchiveEntry> entries)
    {
        if (entries.Count == 0) return 0;
        if (entries.Any(entry => entry.EventId == null) || entries.Select(entry => entry.Id).Distinct().Count() != entries.Count)
            throw new ArgumentException("Each archive must have a distinct readable identity.");
        using SqliteTransaction transaction = _connection.BeginTransaction();
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM archive WHERE id=$id AND closed_at=$time AND {ArchiveEventId}=$event";
        foreach (RaceArchiveEntry entry in entries)
        {
            command.Parameters.Clear(); command.Parameters.AddWithValue("$id", entry.Id);
            command.Parameters.AddWithValue("$time", entry.ClosedAt);
            command.Parameters.AddWithValue("$event", entry.EventId!.Value.ToString());
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("An archive changed or was removed; nothing was deleted. Refresh and retry.");
        }
        transaction.Commit();
        return entries.Count;
    }
}

internal static class ArchivedRaceReports
{
    internal static HeatRaceReport Create(RaceRecoveryFrame frame)
    {
        if (frame.Qualifying.State != QualifyingState.Inactive || frame.Heat.State != HeatRaceState.Complete ||
            frame.Heat.HeatNumber != frame.Heat.TotalHeats)
            throw new InvalidOperationException("This is an unfinished event. It cannot produce a final report.");
        try
        {
            HeatRaceController controller = new(); controller.RestoreRecovery(frame.Heat);
            return controller.CreateReport() with { CreatedLocal = (frame.ExportPlan?.CreatedAt ?? frame.SavedAt).DateTime };
        }
        catch (Exception ex) when (ex is NullReferenceException or IndexOutOfRangeException)
        { throw new InvalidDataException("Archived scoring data cannot produce a report. Preserve it for diagnosis.", ex); }
    }

    internal static RaceExportPaths ExportCopy(RaceRecoveryFrame frame, string outputDirectory, RaceExportOptions options)
    {
        HeatRaceReport report = Create(frame);
        report = report with { Notes = report.Notes + $" Regenerated from archived event {frame.EventId} on {DateTimeOffset.Now:O}; saved scoring snapshot {frame.SavedAt:O}." };
        string directory = Path.Combine(Path.GetFullPath(outputDirectory), $"ArchiveCopy_{frame.EventId:N}_{Guid.NewGuid():N}");
        RaceExportPlan plan = new(frame.EventId, frame.ExportPlan?.CreatedAt ?? frame.SavedAt, directory, options);
        return RaceArchiveWriter.Write(report, exportPlan: plan);
    }

    internal static string OriginalHtml(RaceRecoveryFrame frame)
    {
        RaceExportPlan plan = frame.ExportPlan ?? throw new InvalidOperationException("No original export location was saved. Use Export Copy instead.");
        return Path.Combine(plan.OutputDirectory, RaceArchiveWriter.FileBaseName(frame.Heat.Format, plan.CreatedAt.DateTime, plan.EventId) + ".html");
    }
}
