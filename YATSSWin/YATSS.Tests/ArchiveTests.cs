using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using YATSS;

internal static class ArchiveTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Race archives: " + message); }

    private static RaceRecoveryFrame Completed(string name, bool distance = false, bool grouped = false)
    {
        var qualifiers = new[] { new QualifyingResult("A", 0, 5000), new QualifyingResult("B", 1, 6000) }
            .Select((item, index) => distance ? item with { Distance = DistanceScoring.Approve(10, 50, 70 - index, "qualifying") } : item).ToArray();
        var groups = distance || grouped ? QualifyingController.BuildGroups(qualifiers, new[] { 0, 1 }, 2) : null;
        HeatRaceController heat = new(); LapRace race = new();
        heat.Configure(30, 60, new[] { "A", "B" }, 2, raceName: name, qualifyingResults: qualifiers,
            format: distance ? RaceFormat.CombinedDistance : RaceFormat.HeatRace, groups: groups,
            scheduling: grouped ? HeatRaceScheduling.IndependentGroups : HeatRaceScheduling.RotationQueue);
        void Cross(uint start)
        {
            for (int lane = 0; lane < 2; lane++)
                for (uint index = 0; index < 3; index++)
                {
                    var edge = heat.PrepareEdge(new(lane, index + 1, start + 1000 + index * 5000));
                    race.Process(edge.Edge, edge.CountFirstEdgeAsLap, edge.FastestLapEligible, edge.FirstLapMilliseconds);
                }
        }
        for (int segment = 1; segment <= 2; segment++)
        {
            uint start = (uint)(segment * 100000);
            race.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts); heat.Start(start); Cross(start); heat.Pause(start + 12000);
            if (segment == 1)
            {
                heat.RerunCurrentHeat(race.GetLaneSnapshots(), "Discarded attempt");
                race.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts); heat.Start(start + 20000); Cross(start + 20000); heat.Pause(start + 32000);
            }
            race.AdjustLapCount(0, -1); heat.RecordManualLapAdjustment(0, -1, race.GetLapCounts()[0]);
            heat.Complete(); heat.RecordHeatResults(race.GetLaneSnapshots());
            if (heat.HasMoreHeats) heat.PrepareNextHeat(race.GetLapCounts());
        }
        if (distance) foreach (var candidate in heat.GetFinalDistanceCandidates()) heat.ApproveFinalDistance(candidate.RacerId, 25, "finish");
        Guid id = Guid.NewGuid();
        return new(RaceRecoveryStore.SchemaVersion, id, DateTimeOffset.Now, "Archive fixture", heat.CaptureRecovery(0),
            new QualifyingController().CaptureRecovery(), race.Options, race.CaptureRecovery(), true, true, new[] { "A", "B" }, 1800);
    }

    internal static void Run()
    {
        string directory = Path.Combine(TestWorkspace.Root, "ArchiveTests"); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ActiveRace.db");
        using RaceRecoveryStore store = new(path);
        var completed = Completed("Saturday <race>");
        var plan = new RaceExportPlan(completed.EventId, DateTimeOffset.Now, Path.Combine(directory, "original"), new());
        completed = completed with { ExportPlan = plan };
        HeatRaceReport report = ArchivedRaceReports.Create(completed);
        var originals = RaceArchiveWriter.Write(report, exportPlan: plan);
        store.Save(completed); store.Archive("Completed event");
        Check(store.ListArchives().Count == 1 && store.Read() == null, "archive contains one completed event");
        RaceArchiveEntry first = store.ListArchives().Entries.Single();
        Check(first.Status == "Completed / exported" && first.Demo && first.RaceName == "Saturday <race>", "summary metadata");
        Check(store.ListArchives("SATURDAY").Count == 1 && store.ListArchives("not found").Count == 0 && store.ListArchives("Completed event").Count == 1, "search names/reasons");
        var saved = store.ReadArchive(first);
        string frozen = JsonSerializer.Serialize(saved);
        var rebuilt = ArchivedRaceReports.Create(saved);
        Check(rebuilt.Racers.Select(item => item.TotalLaps).SequenceEqual(report.Racers.Select(item => item.TotalLaps)) &&
            rebuilt.Reruns.Count == 1 && rebuilt.ManualAdjustments.Count == 2 && rebuilt.QualifyingResults.Count == 2, "report reconstruction preserves scoring and audits");
        Check(ArchivedRaceReports.OriginalHtml(saved) == originals.Html, "original report location matches exporter");
        string originalHtml = File.ReadAllText(originals.Html);
        File.Delete(originals.Json!);
        var copy = ArchivedRaceReports.ExportCopy(saved, directory, new(true, false));
        var secondCopy = ArchivedRaceReports.ExportCopy(saved, directory, new(false, true));
        Check(copy.Html != secondCopy.Html && copy.Json != null && copy.ResultsCsv == null && secondCopy.Json == null && secondCopy.ResultsCsv != null,
            "copies have unique folders and honor options");
        Check(File.ReadAllText(originals.Html) == originalHtml && !File.Exists(originals.Json!) && JsonSerializer.Serialize(store.ReadArchive(first)) == frozen,
            "copies never overwrite originals or saved snapshots");
        var distance = Completed("Distance", distance: true); store.Save(distance); store.Archive("Distance completed");
        var distanceReport = ArchivedRaceReports.Create(store.ReadArchive(store.ListArchives().Entries[0]));
        Check(distanceReport.DistanceStandings.Count == 2 && distanceReport.FinalDistanceApprovals.Count == 2, "Combined Distance reconstruction");
        var grouped = Completed("Grouped", grouped: true); store.Save(grouped); store.Archive("Grouped completed");
        Check(ArchivedRaceReports.Create(store.ReadArchive(store.ListArchives().Entries[0])).Scheduling == HeatRaceScheduling.IndependentGroups, "independent scheduling retained");
        HeatRaceController pending = new(); pending.Configure(30, 60, new[] { "A", "B" }, 2);
        var unfinished = completed with { EventId = Guid.NewGuid(), ExportPlan = null, ReportWritten = false,
            Heat = pending.CaptureRecovery(0), Lanes = new LapRace().CaptureRecovery() };
        store.Save(unfinished); store.Archive("Director abandoned unfinished race");
        bool refused = false;
        try { ArchivedRaceReports.Create(store.ReadArchive(store.ListArchives().Entries[0])); } catch (InvalidOperationException) { refused = true; }
        Check(refused && store.ListArchives().Entries[0].Status == "Unfinished", "unfinished races cannot generate final reports");

        store.Save(unfinished with { EventId = Guid.NewGuid() });
        string active = JsonSerializer.Serialize(store.Read());
        using SqliteConnection raw = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); raw.Open();
        using SqliteCommand command = raw.CreateCommand();
        command.CommandText = "UPDATE archive SET closed_at='2000-01-01T00:00:00.0000000+00:00' WHERE id=$id";
        command.Parameters.AddWithValue("$id", first.Id); command.ExecuteNonQuery();
        var old = store.ArchivesOlderThan(DateTimeOffset.UtcNow.AddDays(-1));
        Check(old.Count == 1, "retention uses archive date");
        Check(store.DeleteArchives(old) == 1 && File.Exists(originals.Html) && JsonSerializer.Serialize(store.Read()) == active, "deletion preserves exports and active journal");
        bool stale = false;
        try { store.DeleteArchives(new[] { first }); } catch (InvalidOperationException) { stale = true; }
        Check(stale, "stale selection refused");
        var entries = store.ListArchives().Entries;
        bool rolledBack = false;
        try { store.DeleteArchives(new[] { entries[0], first }); } catch (InvalidOperationException) { rolledBack = true; }
        Check(rolledBack && store.ListArchives().Count == entries.Count, "batch deletion rolls back atomically");
        command.CommandText = "INSERT INTO archive(closed_at,reason,payload) VALUES('2026-01-01','Malformed','not json')";
        command.Parameters.Clear(); command.ExecuteNonQuery();
        Check(store.ListArchives().Entries[0].Status == "Unreadable", "malformed record does not break list");
        bool malformed = false;
        try { store.ReadArchive(store.ListArchives().Entries[0]); } catch (InvalidDataException) { malformed = true; }
        Check(malformed, "malformed payload preserved/refused");
        Check(store.ListArchives(pageSize: 2).Entries.Count == 2 && store.ListArchives(page: 1, pageSize: 2).Entries.Count > 0, "paged listing");
        JsonNode legacy = JsonSerializer.SerializeToNode(completed)!;
        legacy["SchemaVersion"] = 1; legacy["Heat"]!["HeatLengthMinutes"] = 1; legacy["Heat"]!.AsObject().Remove("HeatLengthSeconds");
        command.CommandText = "INSERT INTO archive(closed_at,reason,payload) VALUES($time,'Legacy',$payload)";
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$payload", legacy.ToJsonString()); command.ExecuteNonQuery();
        Check(store.ReadArchive(store.ListArchives().Entries[0]).Heat.HeatLengthSeconds == 60, "legacy archived durations convert on read");
        Console.WriteLine("Archive browsing, reconstruction, export copies, retention, corruption and active-event isolation passed.");
    }

    internal static void CheckUi(string path)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                var entry = new RaceArchiveEntry(1, DateTimeOffset.UtcNow.ToString("O"), "Completed event", Guid.NewGuid(), "Saturday Club Race", "HeatRace", "Completed / exported", 16, 16, false);
                using RaceArchiveBrowser form = new((_, _) => new(new[] { entry }, 1), _ => throw new Exception("Unexpected read"),
                    _ => Array.Empty<RaceArchiveEntry>(), _ => throw new Exception("Unexpected deletion"), _ => { }, new());
                form.Size = form.MinimumSize; form.Show(); Application.DoEvents();
                using Bitmap bitmap = new(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path);
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure != null) throw failure;
    }
}
