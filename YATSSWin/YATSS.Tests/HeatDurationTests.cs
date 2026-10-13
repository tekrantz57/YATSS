using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using YATSS;

internal static class HeatDurationTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Heat seconds: " + message); }

    public static void Run()
    {
        Check(HeatRaceSetup.DefaultBetweenHeatsSeconds == 60, "new heat setup defaults to a one-minute intermission");
        using (SqliteConnection settings = new("Data Source=:memory:"))
        {
            settings.Open();
            using SqliteCommand command = settings.CreateCommand();
            command.CommandText = "CREATE TABLE heat_race_settings(id INTEGER PRIMARY KEY, heat_length_seconds INTEGER, between_heats_seconds INTEGER, scheduling INTEGER DEFAULT 0); CREATE TABLE heat_race_identity(id INTEGER PRIMARY KEY, race_name TEXT);";
            command.ExecuteNonQuery();
            HeatRaceSetupSettings defaults = new(180, HeatRaceSetup.DefaultBetweenHeatsSeconds, "");
            Check(AppDatabase.LoadHeatRaceSettings(settings, defaults).BetweenHeatsSeconds == 60,
                "no saved settings uses sixty seconds");
            foreach (int seconds in new[] { 0, 15, 60, 120, 300 })
            {
                command.CommandText = "INSERT OR REPLACE INTO heat_race_settings(id,heat_length_seconds,between_heats_seconds) VALUES(1,90,$seconds)";
                command.Parameters.Clear(); command.Parameters.AddWithValue("$seconds", seconds);
                command.ExecuteNonQuery();
                HeatRaceSetupSettings loaded = AppDatabase.LoadHeatRaceSettings(settings, defaults);
                Check(loaded.BetweenHeatsSeconds == seconds && loaded.HeatLengthSeconds == 90,
                    "explicit saved intermission preserved: " + seconds);
            }
        }
        foreach (int seconds in new[] { 1, 30, 90, 180, 450, 86400 })
        {
            HeatRaceController heat = new();
            heat.Configure(seconds, 10, new[] { "A", "B" }, 2);
            heat.Start(1000);
            uint end = (uint)(1000 + seconds * 1000);
            Check(!heat.IsExpired(end - 1) && heat.IsExpired(end), "exact expiration for " + seconds);
            heat.Pause(1500);
            Check(heat.GetRemaining(50000).TotalMilliseconds == seconds * 1000 - 500, "pause excludes stopped time");
            HeatRaceController restored = new();
            restored.RestoreRecovery(heat.CaptureRecovery(1500));
            Check(restored.GetRemaining(0) == heat.GetRemaining(1500), "recovery retains seconds and elapsed time");
            restored.Resume(100);
            Check(restored.IsExpired((uint)(100 + seconds * 1000 - 500)), "resume retains exact expiration");
            restored.Complete();
            Check(restored.PrepareNextHeat(new int[8]) && restored.GetRemaining(0).TotalSeconds == seconds,
                "rotation resets the full configured duration");
        }

        HeatRaceController minimum = new(); minimum.Configure(0, 0, new[] { "A", "B" });
        Check(minimum.HeatLengthSeconds == 1, "minimum clamps to one second");
        HeatRaceController distance = new();
        distance.Configure(90, 0, new[] { "A", "B" }, format: RaceFormat.CombinedDistance);
        Check(distance.HeatLengthSeconds == 90 && distance.CaptureRecovery(0).HeatLengthSeconds == 90,
            "Combined Distance uses the same second-based segment duration");

        using (SqliteConnection database = new("Data Source=:memory:"))
        {
            database.Open();
            using SqliteCommand command = database.CreateCommand();
            command.CommandText = "CREATE TABLE heat_race_settings(id INTEGER PRIMARY KEY, heat_length_minutes INTEGER, between_heats_seconds INTEGER); INSERT INTO heat_race_settings VALUES(1,3,15);";
            command.ExecuteNonQuery();
            MethodInfo upgrade = typeof(HeatRaceController).Assembly.GetType("YATSS.DatabaseMaintenance")!
                .GetMethod("UpgradeHeatDuration", BindingFlags.NonPublic | BindingFlags.Static)!;
            upgrade.Invoke(null, new object[] { database });
            upgrade.Invoke(null, new object[] { database });
            command.CommandText = "SELECT heat_length_seconds FROM heat_race_settings WHERE id=1";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 180, "old minutes convert exactly once");
            command.CommandText = "UPDATE heat_race_settings SET heat_length_seconds=90; SELECT heat_length_seconds FROM heat_race_settings";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 90, "mixed-minute duration persists exactly");
        }

        string directory = Path.Combine(Path.GetTempPath(), "yatss-heat-seconds-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            HeatRaceController heat = new(); heat.Configure(90, 15, new[] { "A", "B" }, 2);
            heat.Start(1000); heat.Pause(31500);
            string path = Path.Combine(directory, "ActiveRace.db");
            RaceRecoveryFrame frame = new(RaceRecoveryStore.SchemaVersion, Guid.NewGuid(), DateTimeOffset.Now,
                "Seconds test", heat.CaptureRecovery(31500), new QualifyingController().CaptureRecovery(),
                LapRaceOptions.Default, new LapRace().CaptureRecovery(), false, false, new[] { "A", "B" }, 1800);
            using (RaceRecoveryStore store = new(path)) store.Save(frame);
            using (RaceRecoveryStore store = new(path))
                Check(store.Read()!.Heat.HeatLengthSeconds == 90, "journal retains non-minute duration");

            JsonNode legacy = JsonSerializer.SerializeToNode(frame)!;
            legacy["SchemaVersion"] = 1;
            legacy["Heat"]!["HeatLengthMinutes"] = 3;
            legacy["Heat"]!.AsObject().Remove("HeatLengthSeconds");
            using (SqliteConnection database = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                database.Open(); using SqliteCommand command = database.CreateCommand();
                command.CommandText = "UPDATE recovery_version SET version=1; UPDATE journal SET payload=$payload";
                command.Parameters.AddWithValue("$payload", legacy.ToJsonString()); command.ExecuteNonQuery();
            }
            using (RaceRecoveryStore store = new(path))
            {
                RaceRecoveryFrame converted = store.Read()!;
                Check(converted.Heat.HeatLengthSeconds == 180 && converted.Heat.ElapsedMilliseconds == 30500,
                    "legacy journal converts duration without changing elapsed time");
                store.Save(converted);
            }
            using (RaceRecoveryStore store = new(path))
                Check(store.Read()!.Heat.HeatLengthSeconds == 180, "mixed journal versions do not double-convert");

            heat.RecordHeatResults(new LapRace().GetLaneSnapshots());
            RaceExportPaths exports = RaceArchiveWriter.Write(heat.CreateReport(), directory);
            using JsonDocument archive = JsonDocument.Parse(File.ReadAllText(exports.Json!));
            Check(archive.RootElement.GetProperty("race").GetProperty("heatLengthSeconds").GetInt32() == 90,
                "JSON duration uses seconds");
            Check(File.ReadAllText(exports.Html).Contains("90 second(s)"), "HTML duration uses seconds");
            Check(File.ReadAllText(exports.ResultsCsv!).Contains("HeatLengthSeconds") &&
                File.ReadAllLines(exports.ResultsCsv!).Skip(1).All(line => line.EndsWith(",90")), "CSV duration uses seconds");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
