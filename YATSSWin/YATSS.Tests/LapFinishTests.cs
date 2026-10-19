using System.Reflection;
using System.Text.Json;
using YATSS;

internal static class LapFinishTests
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Lap Finish: " + message); }

    private static HeatRaceController Create(int target = 2, int lanes = 2)
    {
        HeatRaceController heat = new();
        heat.Configure(1, 60, Enumerable.Range(0, lanes).Select(i => "Racer " + i).ToArray(), lanes,
            raceName: "Lap <Finish>", format: RaceFormat.LapFinish, lapTarget: target);
        return heat;
    }

    private static LapUpdate? Cross(HeatRaceController heat, LapRace race, int lane, uint sequence, uint time)
    {
        var decision = heat.PrepareEdge(new(lane, sequence, time));
        if (!decision.ShouldProcess) return null;
        var update = race.Process(decision.Edge, decision.CountFirstEdgeAsLap,
            decision.FastestLapEligible, decision.FirstLapMilliseconds);
        if (update.Kind is LapUpdateKind.Counted or LapUpdateKind.MissedFrame)
            heat.TryCompleteLapFinish(lane, race.GetLapCounts()[lane], time);
        if (heat.State == HeatRaceState.Complete) heat.RecordHeatResults(race.GetLaneSnapshots());
        return update;
    }

    public static void Run()
    {
        foreach (int target in new[] { 0, -1, HeatRaceController.MaximumLapTarget + 1 })
        {
            bool rejected = false;
            try { Create(target); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid target rejected: " + target);
        }
        foreach (var racers in new[] { new[] { "Only" }, new[] { "A", "B", "C" }, new[] { "A", "a" } })
        {
            bool rejected = false;
            try { new HeatRaceController().Configure(60, 60, racers, 2, format: RaceFormat.LapFinish); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid or duplicate entrants rejected");
        }
        HeatRaceController heat = Create(); LapRace race = new();
        Check(heat.TotalHeats == 1 && !heat.HasMoreHeats && !heat.StartsAtLine, "one race without seeded first lap");
        heat.Start(1000);
        Check(!heat.IsExpired(200000) && !heat.Complete(), "timed expiration cannot end Lap Finish");
        Check(Cross(heat, race, 0, 1, 2000)!.Kind == LapUpdateKind.Started && race.GetLapCounts()[0] == 0,
            "first crossing establishes baseline, not a lap");
        Cross(heat, race, 1, 1, 2000);
        Check(Cross(heat, race, 0, 1, 3000)!.Kind == LapUpdateKind.Duplicate, "duplicate cannot count");
        Check(Cross(heat, race, 0, 2, 2100)!.Kind == LapUpdateKind.TooFast, "too-fast crossing cannot count");
        Cross(heat, race, 1, 2, 4000); Cross(heat, race, 0, 3, 5000);
        Check(heat.State == HeatRaceState.Running, "race continues below target");
        heat.Pause(5500);
        Check(Cross(heat, race, 1, 3, 6000) == null, "track call ignores crossings");
        Check(heat.GetElapsed(90000).TotalMilliseconds == 4500, "pause freezes elapsed");
        HeatRaceController restored = new(); restored.RestoreRecovery(heat.CaptureRecovery(5500));
        Check(restored.State == HeatRaceState.Paused && restored.LapTarget == 2 && restored.GetElapsed(0).TotalMilliseconds == 4500,
            "recovery preserves target and elapsed beyond ignored duration");
        heat.Resume(10000);
        Cross(heat, race, 0, 4, 12000);
        Check(heat.State == HeatRaceState.Complete && heat.LapFinish == new LapFinishOutcome(0, 6500),
            "first valid target crossing freezes winner and active elapsed");
        Check(!heat.CanAdjustLapCounts && Cross(heat, race, 1, 4, 12000) == null && Cross(heat, race, 0, 5, 14000) == null,
            "same-time and late frames cannot change finished results");
        var report = heat.CreateReport();
        Check(report.Format == RaceFormat.LapFinish && report.LapTarget == 2 && report.HeatLengthSeconds == 0 &&
            report.BetweenHeatsSeconds == 0 && report.LapFinishStandings[0].Winner && report.Racers[0].RacerName == "Racer 0",
            "report describes target rather than a duration");
        Check(report.LapFinishStandings.Select(row => row.CompletedLaps).SequenceEqual(new[] { 2, 1 }), "report freezes all counts");
        Files(heat, race, report);
        Check(heat.RerunCurrentHeat(race.GetLaneSnapshots(), "Bench rerun"), "core rerun accepts finished unpublished race");
        race.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts);
        Check(heat.LapFinish == null && heat.LapTarget == 2 && heat.State == HeatRaceState.Ready && race.GetLapCounts().All(n => n == 0),
            "rerun clears outcome, not target or lane assignment");
        heat.Start(20000);
        Check(Cross(heat, race, 0, 1, 21000)!.Kind == LapUpdateKind.Started, "rerun starts with a baseline again");

        HeatRaceController ties = Create(2, 4); LapRace tieRace = new(); ties.Start(1000);
        for (int lane = 0; lane < 4; lane++) Cross(ties, tieRace, lane, 1, 2000);
        Cross(ties, tieRace, 3, 2, 5000); Cross(ties, tieRace, 2, 2, 4000); Cross(ties, tieRace, 1, 2, 4000);
        Cross(ties, tieRace, 0, 2, 4500); Cross(ties, tieRace, 0, 3, 6500);
        var standings = ties.CreateReport().LapFinishStandings;
        Check(standings.Select(row => row.LaneIndex).SequenceEqual(new[] { 0, 1, 2, 3 }) &&
            standings.Select(row => row.Place).SequenceEqual(new[] { 1, 2, 2, 4 }), "last crossing orders equal laps and exact ties share place");
        HeatRaceController wrap = Create(1); LapRace wrapRace = new(); wrap.Start(uint.MaxValue - 3000);
        Cross(wrap, wrapRace, 0, 1, uint.MaxValue - 2000); Cross(wrap, wrapRace, 0, 2, 1000);
        Check(wrap.LapFinish?.ElapsedMilliseconds == 4001, "controller wrap retains winning elapsed");
        Settings(); Ui();
        Console.WriteLine("Lap Finish scoring, cutoff, ties, pause, recovery, rerun, setup and exports passed.");
    }

    private static void Files(HeatRaceController heat, LapRace race, HeatRaceReport report)
    {
        string directory = Path.Combine(TestWorkspace.Root, "lap-finish"); Directory.CreateDirectory(directory);
        using (RaceRecoveryStore store = new(Path.Combine(directory, "ActiveRace.db")))
        {
            store.Save(new(RaceRecoveryStore.SchemaVersion, Guid.NewGuid(), DateTimeOffset.Now, "Finished",
                heat.CaptureRecovery(99999), new QualifyingController().CaptureRecovery(), race.Options,
                race.CaptureRecovery(), false, false, new[] { "Racer 0", "Racer 1" }, 1800));
            var saved = store.Read()!;
            HeatRaceController loaded = new(); loaded.RestoreRecovery(saved.Heat);
            Check(loaded.LapFinish == heat.LapFinish && loaded.CreateReport().LapFinishStandings.SequenceEqual(report.LapFinishStandings),
                "durable finish survives process restart");
            bool rejected = false;
            try { new HeatRaceController().RestoreRecovery(saved.Heat with { LapFinish = null }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "completed recovery requires a winner");
            store.Archive("Completed Lap Finish");
            var entry = store.ListArchives().Entries.Single();
            var archived = store.ReadArchive(entry);
            Check(entry.Format == "Lap Finish" && ArchivedRaceReports.Create(archived).LapFinish == report.LapFinish,
                "archive browsing and report reconstruction retain format and winner");
            Check(File.Exists(ArchivedRaceReports.ExportCopy(archived, directory, new()).Html), "archived results can be exported again");
        }
        var files = RaceArchiveWriter.Write(report, directory);
        using var json = JsonDocument.Parse(File.ReadAllText(files.Json!));
        var exported = json.RootElement.GetProperty("race");
        Check(json.RootElement.GetProperty("schemaVersion").GetInt32() == 4 && exported.GetProperty("format").GetString() == "LapFinish" &&
            exported.GetProperty("lapFinish").GetProperty("elapsedMilliseconds").GetInt64() == 6500 &&
            exported.GetProperty("lapFinishStandings")[0].GetProperty("winner").GetBoolean(), "JSON has explicit outcome and ordered standings");
        string html = File.ReadAllText(files.Html);
        Check(html.Contains("Lap Finish Results") && html.Contains("Lap &lt;Finish&gt;") && html.Contains("Last Counted Crossing"), "HTML target, escaped name and finish order");
        string[] csv = File.ReadAllLines(files.ResultsCsv!);
        Check(csv[0].EndsWith("RaceFormat,LapTarget,RaceElapsedMilliseconds,LastCrossingElapsedMilliseconds,Winner") &&
            csv[1].Contains(",LapFinish,2,6500,") && Path.GetFileName(files.Html).StartsWith("LapFinish_"), "CSV contract and filename identify format");
    }

    private static T Field<T>(object form, string name) => (T)form.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
    private static void Settings()
    {
        bool wasOpen = AppDatabase.Connection.State == System.Data.ConnectionState.Open;
        AppDatabase.Open();
        HeatRaceSetupSettings previous = AppDatabase.LoadHeatRaceSettings(new(180, 60, ""));
        try
        {
            HeatRaceSetupSettings chosen = new(90, 45, "Saved target")
            { Format = RaceFormat.LapFinish, LapTarget = 125, Scheduling = HeatRaceScheduling.IndependentGroups };
            AppDatabase.SaveHeatRaceSettings(chosen);
            Check(AppDatabase.LoadHeatRaceSettings(new(180, 60, "")) == chosen,
                "format and target round-trip without losing timed-race settings");
        }
        finally
        {
            AppDatabase.SaveHeatRaceSettings(previous);
            if (!wasOpen) AppDatabase.Connection.Close();
        }
    }
    private static void Ui()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using HeatRaceSetup form = new(2, LaneConfiguration.CreateDefaults(), new[] { "Alice", "Bob", "Carol" },
                    new(90, 60, "Lap Finish setup") { Format = RaceFormat.LapFinish, LapTarget = 30 });
                form.Size = form.MinimumSize; form.Show(); Application.DoEvents();
                Check(form.Format == RaceFormat.LapFinish && form.LapTarget == 30 && Field<NumericUpDown>(form, "_lapTarget").Visible &&
                    !Field<NumericUpDown>(form, "_heatLengthSeconds").Visible, "saved format/target shown instead of heat duration");
                var racers = Field<CheckedListBox>(form, "_racerList");
                racers.SetItemChecked(0, true); racers.SetItemChecked(1, true); Application.DoEvents();
                Check(Field<Button>(form, "_okButton").Enabled, "two racers may start");
                racers.SetItemChecked(2, true); Application.DoEvents();
                Check(!Field<Button>(form, "_okButton").Enabled, "excess entrants blocked without hidden queue");
                racers.SetItemChecked(2, false); Application.DoEvents();
                var target = Field<NumericUpDown>(form, "_lapTarget");
                Check(target.Right <= target.Parent!.ClientSize.Width && target.Bottom <= target.Parent.ClientSize.Height, "target fits minimum-size layout");
                using Bitmap image = new(form.Width, form.Height); form.DrawToBitmap(image, new(0, 0, form.Width, form.Height));
                image.Save(Path.Combine(TestWorkspace.Root, "lap-finish-setup.png"));
                Field<ComboBox>(form, "_format").SelectedIndex = 0; Application.DoEvents();
                Check(Field<NumericUpDown>(form, "_heatLengthSeconds").Value == 90 && Field<NumericUpDown>(form, "_heatLengthSeconds").Visible,
                    "switching formats preserves saved timed-race choices");
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
}
