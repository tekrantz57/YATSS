using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using YATSS;

internal static class GroupedHeatTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Independent heat groups: " + message); }

    public static void Run()
    {
        Settings();
        HeatRaceController pending = new();
        pending.Configure(30, 60, new[] { "A", "B", "C" }, 2, scheduling: HeatRaceScheduling.IndependentGroups);
        Check(!pending.Start(1000) && pending.GetOccupiedLaneMask() == 0, "unqualified groups cannot start or assign lanes");
        HeatRaceController restored = new(); restored.RestoreRecovery(pending.CaptureRecovery(0));
        Check(restored.Scheduling == HeatRaceScheduling.IndependentGroups && !restored.Start(1000), "pending qualifying requirement survives restore");
        HeatRaceController queue = new(); queue.Configure(30, 60, new[] { "A", "B", "C" }, 2);
        Check(queue.Scheduling == HeatRaceScheduling.RotationQueue && queue.TotalHeats == 3 && queue.Start(1000), "default queue unchanged");
        var invalidRanks = new[] { new QualifyingResult("A", 0, 5000), new QualifyingResult("B", 1, 6000), new QualifyingResult("C", 2, 7000) };
        bool rejected = false;
        try
        {
            new HeatRaceController().Configure(30, 60, new[] { "A", "B", "C" }, 2,
                qualifyingResults: invalidRanks, groups: new[] { new[] { "A", "C" }, new[] { "B", "" } },
                scheduling: HeatRaceScheduling.IndependentGroups);
        }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "director cannot change consecutive ranked group membership");
        foreach (int racers in new[] { 2, 8, 10, 17 }) Race(racers, 8);
        Race(5, 4);
        Console.WriteLine("Independent heat groups, scoring, corrections, reruns, settings, recovery and exports passed.");
    }

    private static void Settings()
    {
        SqliteConnection connection = AppDatabase.Connection;
        string original = connection.ConnectionString;
        Check(connection.State == System.Data.ConnectionState.Closed, "settings fixture owns a closed connection");
        try
        {
            connection.ConnectionString = "Data Source=:memory:"; connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE heat_race_settings(id INTEGER PRIMARY KEY, heat_length_seconds INTEGER NOT NULL, between_heats_seconds INTEGER NOT NULL); INSERT INTO heat_race_settings VALUES(1,90,15);";
            command.ExecuteNonQuery();
            MethodInfo ensure = typeof(AppDatabase).GetMethod("EnsureSchema", BindingFlags.NonPublic | BindingFlags.Static)!;
            ensure.Invoke(null, null); ensure.Invoke(null, null);
            var loaded = AppDatabase.LoadHeatRaceSettings(new(180, 60, ""));
            Check(loaded.Scheduling == HeatRaceScheduling.RotationQueue && loaded.HeatLengthSeconds == 90 && loaded.BetweenHeatsSeconds == 15,
                "schema extension preserves old settings with queue default");
            AppDatabase.SaveHeatRaceSettings(loaded with { Scheduling = HeatRaceScheduling.IndependentGroups });
            Check(AppDatabase.LoadHeatRaceSettings(new(180, 60, "")).Scheduling == HeatRaceScheduling.IndependentGroups, "saved grouped scheduling round-trips");
            command.CommandText = "PRAGMA user_version";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 6, "schema version six");
        }
        finally { connection.Close(); connection.ConnectionString = original; }
    }

    private static void Race(int racerCount, int laneCount)
    {
        var ranked = Enumerable.Range(0, racerCount).Select(id => new QualifyingResult("R" + id, id, 5000 + id)).ToArray();
        int[] choices = Enumerable.Range(0, racerCount).Select(id => (id + 2) % laneCount).ToArray();
        var groups = QualifyingController.BuildGroups(ranked, choices, laneCount);
        Check(groups.Count == (racerCount + laneCount - 1) / laneCount, "consecutive full groups and remainder");
        HeatRaceController heat = new(); LapRace race = new();
        heat.Configure(30, 60, ranked.Select(item => item.RacerName).ToArray(), laneCount,
            qualifyingResults: ranked, groups: groups, scheduling: HeatRaceScheduling.IndependentGroups);
        Check(heat.TotalHeats == groups.Count * laneCount, "full rotation per group");
        var visited = ranked.ToDictionary(item => item.RacerName, _ => new HashSet<int>());
        var totals = ranked.ToDictionary(item => item.RacerName, _ => 0);
        string directory = Path.Combine(Path.GetTempPath(), "yatss-groups-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Guid eventId = Guid.NewGuid();
            using RaceRecoveryStore store = new(Path.Combine(directory, "ActiveRace.db"));
            void Save()
            {
                store.Save(new(RaceRecoveryStore.SchemaVersion, eventId, DateTimeOffset.Now, "Group test",
                    heat.CaptureRecovery(0, store.Cursor?.Heat.HistoryVersion), new QualifyingController().CaptureRecovery(),
                    race.Options, race.CaptureRecovery(store.Cursor?.Lanes), true, false, ranked.Select(item => item.RacerName).ToArray(), 1800));
            }
            for (int segment = 1; segment <= heat.TotalHeats; segment++)
            {
                var snapshot = heat.GetSnapshot(0);
                Check(snapshot.OnDeckRacer == "", "no cross-group waiting queue");
                race.ResetTimingForHeat(snapshot.LaneLapCounts);
                uint start = (uint)(segment * 100000); Check(heat.Start(start), "start assigned group segment");
                for (int lane = 0; lane < laneCount; lane++)
                {
                    string name = snapshot.LaneRacers[lane];
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        Check((heat.GetOccupiedLaneMask() & (1 << lane)) == 0 && !heat.PrepareEdge(new(lane, 1, start + 1000)).ShouldProcess,
                            "empty lane stays unpowered/unscored");
                        continue;
                    }
                    Check(visited[name].Add(lane), "each racer visits each lane once");
                    Check(ranked.Skip((heat.GroupNumber - 1) * laneCount).Take(laneCount).Any(item => item.RacerName == name), "strongest-first group membership unchanged");
                    for (uint crossing = 0; crossing < 3; crossing++)
                    {
                        var edge = heat.PrepareEdge(new(lane, crossing + 1, start + 1000 + crossing * 2000));
                        Check(edge.CountFirstEdgeAsLap == (crossing == 0 && (segment - 1) % laneCount != 0), "ordinary baseline policy restarts per group");
                        race.Process(edge.Edge, edge.CountFirstEdgeAsLap, edge.FastestLapEligible, edge.FirstLapMilliseconds);
                    }
                }
                heat.Pause(start + 7000);
                if (segment == 2)
                {
                    var before = heat.CaptureRecovery(start + 7000);
                    heat.RerunCurrentHeat(race.GetLaneSnapshots(), "Group rerun");
                    race.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts);
                    Check(heat.GetSnapshot(0).LaneRacers.SequenceEqual(snapshot.LaneRacers) && heat.CaptureRecovery(0).Groups.SelectMany(group => group).SequenceEqual(before.Groups.SelectMany(group => group)), "rerun keeps group assignments");
                    heat.Start(start);
                    for (int lane = 0; lane < laneCount; lane++)
                        if (!string.IsNullOrWhiteSpace(snapshot.LaneRacers[lane]))
                        {
                            var edge = heat.PrepareEdge(new(lane, 1, start + 1000));
                            race.Process(edge.Edge, edge.CountFirstEdgeAsLap, edge.FastestLapEligible, edge.FirstLapMilliseconds);
                        }
                    heat.Pause(start + 7000);
                }
                int occupied = Enumerable.Range(0, laneCount).First(lane => !string.IsNullOrWhiteSpace(snapshot.LaneRacers[lane]));
                if (segment == laneCount)
                {
                    race.AdjustLapCount(occupied, -1);
                    heat.RecordManualLapAdjustment(occupied, -1, race.GetLapCounts()[occupied]);
                }
                heat.Complete(); heat.RecordHeatResults(race.GetLaneSnapshots());
                for (int lane = 0; lane < laneCount; lane++)
                    if (!string.IsNullOrWhiteSpace(snapshot.LaneRacers[lane])) totals[snapshot.LaneRacers[lane]] = race.GetLapCounts()[lane];
                Save();
                var saved = store.Read()!;
                heat.RestoreRecovery(saved.Heat); race.RestoreRecovery(saved.Options, saved.Lanes);
                Check(heat.Scheduling == HeatRaceScheduling.IndependentGroups && heat.GroupNumber == (segment - 1) / laneCount + 1, "group state restored");
                if (heat.HasMoreHeats)
                {
                    Check(heat.PrepareNextHeat(race.GetLapCounts()), "advance without distance approvals");
                    if (segment % laneCount == 0) Check(heat.GetSnapshot(0).LaneLapCounts.All(count => count == 0), "new group starts without prior group totals or qualifying credit");
                }
            }
            var report = heat.CreateReport();
            Check(report.Format == RaceFormat.HeatRace && report.DistanceStandings.Count == 0 && report.FinalDistanceApprovals.Count == 0, "no distance scoring leaks into ordinary race");
            Check(report.Racers.All(item => item.TotalLaps == totals[item.RacerName]) && visited.All(pair => pair.Value.Count == laneCount), "all groups finish with latest racing totals");
            Check(report.Racers.Select(item => item.TotalLaps).SequenceEqual(report.Racers.Select(item => item.TotalLaps).OrderDescending()), "one finish order across groups");
            var exports = RaceArchiveWriter.Write(report, directory);
            Check(File.ReadAllText(exports.Html).Contains("Starting Groups"), "HTML group assignments");
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(exports.Json!));
            Check(json.RootElement.GetProperty("race").GetProperty("scheduling").GetString() == "IndependentGroups" && json.RootElement.GetProperty("race").GetProperty("groups").GetArrayLength() == groups.Count, "JSON scheduling and groups");
            Check(File.ReadAllText(exports.ResultsCsv!).Contains("Scheduling,Group,HeatLengthSeconds"), "CSV group metadata");
        }
        finally { Directory.Delete(directory, true); }
    }
}
