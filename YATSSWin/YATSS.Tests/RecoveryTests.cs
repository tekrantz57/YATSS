using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using YATSS;

internal static class RecoveryTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Recovery: " + message); }

    private static RaceRecoveryFrame Frame(HeatRaceController heat, LapRace laps, QualifyingController qualifying,
        Guid id, RaceRecoveryStore store, uint timestamp, bool reportWritten = false) =>
        new(1, id, DateTimeOffset.Now, "Test", heat.CaptureRecovery(timestamp, store.Cursor?.Heat.HistoryVersion),
            qualifying.CaptureRecovery(), laps.Options, laps.CaptureRecovery(store.Cursor?.Lanes), false,
            reportWritten, new[] { "A", "B", "C" }, 1800);

    public static void CrashFixture(string path)
    {
        HeatRaceController heat = new(); heat.Configure(1, 10, new[] { "A", "B" }, 2);
        LapRace laps = new(new(100, 600000, 155));
        QualifyingController qualifying = new();
        heat.Start(1000);
        laps.Process(new(0, 1, 1000)); laps.Process(new(0, 2, 1500));
        using RaceRecoveryStore store = new(path);
        store.Save(Frame(heat, laps, qualifying, Guid.NewGuid(), store, 1500));
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO journal(payload) VALUES('uncommitted garbage')";
        command.ExecuteNonQuery();
        Console.WriteLine("RECOVERY_FIXTURE_READY"); Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite);
    }

    public static void RunUi()
    {
        Thread thread = new(() =>
        {
            Application.EnableVisualStyles();
            HeatRaceController heat = new(); heat.Configure(4, 60, new[] { "A", "B" }, raceName: "Saturday Enduro");
            heat.Start(1000); heat.Pause(104000);
            RaceRecoveryFrame frame = new(1, Guid.NewGuid(), DateTimeOffset.Now, "UI test", heat.CaptureRecovery(104000),
                new QualifyingController().CaptureRecovery(), LapRaceOptions.Default, new LapRace().CaptureRecovery(),
                true, false, new[] { "A", "B" }, 1800);
            using RaceRecoveryChoiceForm choice = new(frame);
            Console.WriteLine("Recovery UI result: " + choice.ShowDialog());
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    }

    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "yatss-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "ActiveRace.db");
            HeatRaceController heat = new(); heat.Configure(1, 10, new[] { "A", "B", "C" }, 2);
            LapRace laps = new(new(100, 600000, 155));
            QualifyingController qualifying = new();
            Guid id = Guid.NewGuid();
            using (RaceRecoveryStore store = new(path))
            {
                heat.Start(1000);
                store.Save(Frame(heat, laps, qualifying, id, store, 900));
                Check(store.Read()!.Heat.ElapsedMilliseconds == 0, "older confirmed clock must not expire a just-started heat");
                laps.Process(new(0, 1, 1000));
                for (uint index = 1; index <= 300; index++)
                {
                    laps.Process(new(0, index + 1, 1000 + index * 150));
                    RaceRecoveryFrame delta = Frame(heat, laps, qualifying, id, store, 1000 + index * 150);
                    Check(delta.Lanes.Sum(lane => lane.Snapshot.Laps.Count) == 1, "crossings must append deltas, not entire histories");
                    store.Save(delta);
                }
                heat.Pause(47000);
                laps.AdjustLapCount(0, -1);
                heat.RecordManualLapAdjustment(0, -1, laps.GetLapCounts()[0]);
                store.Save(Frame(heat, laps, qualifying, id, store, 47000));
                bool refused = false;
                try { using RaceRecoveryStore second = new(path); } catch (IOException) { refused = true; }
                Check(refused, "second journal owner must be refused");
            }
            using (RaceRecoveryStore store = new(path))
            {
                RaceRecoveryFrame saved = store.Read()!;
                Check(saved.Lanes[0].Snapshot.Laps.Count == 300 && saved.Lanes[0].Snapshot.TotalLapCount == 299,
                    "checkpoint plus deltas must retain full history and negative correction");
                HeatRaceController restoredHeat = new(); restoredHeat.RestoreRecovery(saved.Heat);
                LapRace restoredLaps = new(); restoredLaps.RestoreRecovery(saved.Options, saved.Lanes);
                Check(restoredHeat.State == HeatRaceState.Paused && restoredHeat.GetRemaining(0).TotalMilliseconds == 14000,
                    "restart must preserve remaining active time without old controller clock");
                restoredHeat.Resume(25);
                var decision = restoredHeat.PrepareEdge(new(0, 1, 525));
                LapUpdate first = restoredLaps.Process(decision.Edge, decision.CountFirstEdgeAsLap, decision.FastestLapEligible, decision.FirstLapMilliseconds);
                Check(first.Kind == LapUpdateKind.Counted && first.LapMilliseconds == null && !first.FastestLapEligible,
                    "first crossing after recovery counts once without awarding a fast lap");
                restoredHeat.Complete(); restoredHeat.RecordHeatResults(restoredLaps.GetLaneSnapshots());
                Check(restoredHeat.PrepareNextHeat(restoredLaps.GetLapCounts()), "restored rotation should advance");
                restoredLaps.ResetTimingForHeat(restoredHeat.GetSnapshot(0).LaneLapCounts);
                store.Save(Frame(restoredHeat, restoredLaps, qualifying, id, store, 525));
                Check(store.Read()!.Lanes.All(lane => lane.Snapshot.Laps.Count == 0), "new segment must clear only current timing histories");
                Check(store.Read()!.Heat.Laps!.Length == 301, "completed segment history must survive lane timing reset");
                store.Archive("Test discard"); Check(store.Read() == null, "archive clears active slot");
                using SqliteConnection archive = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
                archive.Open(); using SqliteCommand command = archive.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM archive";
                Check(Convert.ToInt32(command.ExecuteScalar()) == 1, "discard retains an archived event");
            }
            TestQualifyingAndDistance(directory);
            TestWriteFailure(Path.Combine(directory, "Failure.db"));
            TestCrash(Path.Combine(directory, "Crash.db"));
            TestInvalidVersion(Path.Combine(directory, "Future.db"));
            Console.WriteLine("Active-race recovery, journal durability, rotations, approvals, and storage-failure tests passed.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void TestQualifyingAndDistance(string directory)
    {
        QualifyingController qualifying = new(); qualifying.Configure(new[] { "A", "B" }, 0, 60);
        qualifying.Start(0); qualifying.CompleteCurrent(new[] { new LaneLapRecord(1000, true, 1000) }, 60000);
        QualifyingRecovery pending = qualifying.CaptureRecovery();
        QualifyingController restoredQualifier = new(); restoredQualifier.RestoreRecovery(pending);
        Check(restoredQualifier.State == QualifyingState.Ready && restoredQualifier.GetRankedResults()[0].Distance == null,
            "completed qualifier awaiting fraction must remain pending, not rerun");
        restoredQualifier.ApproveLastDistance(70, "Position checked");
        restoredQualifier.Start(100); restoredQualifier.Pause(200);
        QualifyingController interrupted = new(); interrupted.RestoreRecovery(restoredQualifier.CaptureRecovery());
        Check(interrupted.State == QualifyingState.Ready && interrupted.CurrentRacer == "B" &&
            interrupted.GetRankedResults()[0].Distance!.TotalHundredths == 170, "interrupted qualifier reruns while earlier approval survives");
        interrupted.Start(0); interrupted.CompleteCurrent(new[] { new LaneLapRecord(1000, true, 1000) }, 60000);
        interrupted.ApproveLastDistance(70, "Position checked");
        interrupted.SetDistanceTieOrder(new[] { 1, 0 });
        var ranked = interrupted.GetDistanceResults();
        var groups = QualifyingController.BuildGroups(ranked, new[] { 0, 1 }, 2);
        HeatRaceController heat = new(); heat.Configure(1, 0, groups[0], 2,
            qualifyingResults: ranked, format: RaceFormat.CombinedDistance, groups: groups);
        LapRace laps = new(new(100, 600000, 155));
        for (int segment = 0; segment < 2; segment++)
        {
            heat.Start((uint)(segment * 60000));
            for (int lane = 0; lane < 2; lane++)
            {
                var decision = heat.PrepareEdge(new(lane, 1, (uint)(segment * 60000 + 1000)));
                laps.Process(decision.Edge, true, true, 1000);
            }
            heat.Complete(); heat.RecordHeatResults(laps.GetLaneSnapshots());
            if (segment == 0) { heat.PrepareNextHeat(laps.GetLapCounts()); laps.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts); }
        }
        int firstId = heat.GetFinalDistanceCandidates()[0].RacerId;
        heat.ApproveFinalDistance(firstId, 35, "Finish position");
        string path = Path.Combine(directory, "Distance.db"); Guid id = Guid.NewGuid();
        using (RaceRecoveryStore store = new(path)) store.Save(Frame(heat, laps, new(), id, store, 120000));
        using (RaceRecoveryStore store = new(path))
        {
            HeatRaceController recovered = new(); recovered.RestoreRecovery(store.Read()!.Heat);
            Check(recovered.State == HeatRaceState.Complete && recovered.GetFinalDistanceCandidates().Count == 1,
                "only unapproved final fraction should remain pending");
            Check(recovered.GetDistanceStandings(0).Single(row => row.RacerId == firstId).FinalPartialHundredths == 35,
                "approved fraction must not be added twice");
            int secondId = recovered.GetFinalDistanceCandidates()[0].RacerId;
            recovered.ApproveFinalDistance(secondId, 35, "Finish position");
            recovered.SetFinalTieOrder(new[] { 1, 0 });
            store.Save(Frame(recovered, laps, new(), id, store, 120000, true));
            HeatRaceController complete = new(); complete.RestoreRecovery(store.Read()!.Heat);
            Check(complete.FinalDistancesConfirmed && complete.CreateReport().FinalTieOrderRecordedAt != null,
                "final approvals and tie decision must survive");
            Check(store.Read()!.ReportWritten, "successful export marker must survive");
        }
    }

    private static void TestWriteFailure(string path)
    {
        HeatRaceController heat = new(); heat.Configure(1, 0, new[] { "A", "B" }, 2);
        LapRace laps = new(); QualifyingController qualifying = new(); Guid id = Guid.NewGuid();
        using RaceRecoveryStore store = new(path); store.Save(Frame(heat, laps, qualifying, id, store, 0));
        using SqliteConnection blocker = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        blocker.Open(); using SqliteTransaction transaction = blocker.BeginTransaction();
        bool failed = false;
        try { store.Save(Frame(heat, laps, qualifying, id, store, 0)); }
        catch (SqliteException) { failed = true; }
        Check(failed && store.Read()!.EventId == id, "failed transaction must leave last committed event intact");
    }

    private static void TestCrash(string path)
    {
        string executable = Environment.ProcessPath!;
        ProcessStartInfo start = new(executable) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--recovery-crash-fixture"); start.ArgumentList.Add(path);
        using Process child = Process.Start(start)!;
        try
        {
            Task<string?> ready = child.StandardOutput.ReadLineAsync();
            Check(ready.Wait(TimeSpan.FromSeconds(20)) && ready.Result == "RECOVERY_FIXTURE_READY", "crash fixture must initialize");
        }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); child.WaitForExit(); }
        using RaceRecoveryStore store = new(path);
        Check(store.Read()!.Lanes[0].Snapshot.TotalLapCount == 1, "forced termination must retain committed lap and reject uncommitted garbage");
        HeatRaceController restored = new(); restored.RestoreRecovery(store.Read()!.Heat);
        Check(restored.State == HeatRaceState.Paused, "running event must recover paused");
    }

    private static void TestInvalidVersion(string path)
    {
        using (SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE recovery_version(version INTEGER); INSERT INTO recovery_version VALUES(99);";
            command.ExecuteNonQuery();
        }
        bool refused = false;
        try { using RaceRecoveryStore store = new(path); } catch (InvalidDataException) { refused = true; }
        Check(refused, "future journal schema must be preserved and refused");
    }
}
