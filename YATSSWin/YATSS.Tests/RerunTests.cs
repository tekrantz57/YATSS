using System.Text.Json;
using YATSS;

internal static class RerunTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Heat rerun: " + message); }

    private static void Cross(HeatRaceController heat, LapRace race, uint start, int count = 3)
    {
        for (int lane = 0; lane < 2; lane++)
            for (uint index = 0; index < count; index++)
            {
                var edge = heat.PrepareEdge(new(lane, index + 1, start + 1000 + index * 2000));
                Check(edge.ShouldProcess, "occupied running lane accepts crossing");
                race.Process(edge.Edge, edge.CountFirstEdgeAsLap, edge.FastestLapEligible, edge.FirstLapMilliseconds);
            }
    }

    public static void Run()
    {
        HeatRaceController heat = new();
        LapRace race = new();
        heat.Configure(30, 10, new[] { "A", "B", "C" }, 2);
        Check(!heat.CanRerunCurrentHeat && !heat.RerunCurrentHeat(race.GetLaneSnapshots(), "unused"), "ready heat cannot rerun");
        heat.Start(1000); Cross(heat, race, 1000);
        Check(!heat.CanRerunCurrentHeat, "running heat cannot rerun");
        heat.Complete(); heat.RecordHeatResults(race.GetLaneSnapshots());
        var previous = heat.CreateReport();
        heat.PrepareNextHeat(race.GetLapCounts());
        var starting = heat.GetSnapshot(0);
        race.ResetTimingForHeat(starting.LaneLapCounts);
        heat.Start(100000); Cross(heat, race, 100000);
        heat.Pause(106000);
        race.AdjustLapCount(0, -1); heat.RecordManualLapAdjustment(0, -1, race.GetLapCounts()[0]);

        string directory = Path.Combine(Path.GetTempPath(), "yatss-rerun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "ActiveRace.db");
            Guid id = Guid.NewGuid();
            using (RaceRecoveryStore store = new(path))
            {
                RaceRecoveryFrame Frame() => new(RaceRecoveryStore.SchemaVersion, id, DateTimeOffset.Now,
                    "Rerun test", heat.CaptureRecovery(106000, store.Cursor?.Heat.HistoryVersion),
                    new QualifyingController().CaptureRecovery(), race.Options, race.CaptureRecovery(store.Cursor?.Lanes),
                    true, false, new[] { "A", "B", "C" }, 1800);
                store.Save(Frame());
                var saved = store.Read()!;
                heat.RestoreRecovery(saved.Heat); race.RestoreRecovery(saved.Options, saved.Lanes);
                Check(heat.RerunCurrentHeat(race.GetLaneSnapshots(), "Fault <test>, rerun"), "paused restored heat reruns");
                race.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts);
                store.Save(Frame());
                for (int index = 0; index < 260; index++) store.Save(Frame());
                Check(heat.CaptureRecovery(0, store.Cursor!.Heat.HistoryVersion).Reruns == null,
                    "ordinary checkpoint omits unchanged discarded history");
            }
            using (RaceRecoveryStore store = new(path))
            {
                var saved = store.Read()!;
                heat.RestoreRecovery(saved.Heat); race.RestoreRecovery(saved.Options, saved.Lanes);
                Check(heat.State == HeatRaceState.Ready && heat.GetRemaining(0).TotalSeconds == 30, "restart waits with full duration");
                Check(heat.GetSnapshot(0).LaneRacers.SequenceEqual(starting.LaneRacers) &&
                    heat.GetSnapshot(0).OnDeckRacer == starting.OnDeckRacer, "lanes and waiting queue unchanged");
                Check(race.GetLapCounts().SequenceEqual(starting.LaneLapCounts), "starting totals restored");
                Check(race.GetLaneSnapshots().All(item => item.Laps.Count == 0 && item.BestLapMilliseconds == null), "timing cleared");
                var report = heat.CreateReport();
                Check(report.LaneResults.SequenceEqual(previous.LaneResults) && report.Laps.SequenceEqual(previous.Laps), "prior heat untouched");
                Check(report.ManualAdjustments.Count == 0 && report.Reruns.Count == 1 &&
                    report.Reruns[0].ManualAdjustments.Count == 1 && report.Reruns[0].Laps.Count > 0 &&
                    report.Reruns[0].DiscardedElapsedMilliseconds == 6000, "voided data retained separately through compaction");
            }
            heat.Start(200000); Cross(heat, race, 200000, 2); heat.Complete();
            Check(heat.RerunCurrentHeat(race.GetLaneSnapshots(), "Second attempt"), "completed heat reruns");
            race.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts);
            heat.Start(300000);
            var decision = heat.PrepareEdge(new(0, 1, 301000));
            Check(decision.CountFirstEdgeAsLap && !decision.FastestLapEligible && decision.Edge.TimestampMillis == 31000,
                "rotation first-edge policy preserved and completed clock base rewound exactly once");
            race.Process(decision.Edge, decision.CountFirstEdgeAsLap, decision.FastestLapEligible, decision.FirstLapMilliseconds);
            heat.Pause(302000); heat.RecordHeatResults(race.GetLaneSnapshots());
            var final = heat.CreateReport();
            Check(final.Reruns.Select(item => item.DiscardedAttempt).SequenceEqual(new[] { 1, 2 }), "repeated attempts numbered");
            Check(final.Laps.Count == previous.Laps.Count + 1 && final.ManualAdjustments.Count == 0, "only replacement laps score");
            var exports = RaceArchiveWriter.Write(final, directory);
            Check(File.ReadAllText(exports.Html).Contains("Fault &lt;test&gt;, rerun"), "HTML escapes audit reason");
            using var json = JsonDocument.Parse(File.ReadAllText(exports.Json!));
            Check(json.RootElement.GetProperty("race").GetProperty("reruns").GetArrayLength() == 2, "JSON contains discarded attempts");
            Check(File.ReadAllText(Directory.GetFiles(directory, "*_reruns.csv").Single()).Contains("VoidedLapsJson"), "CSV contains full audit");
        }
        finally { Directory.Delete(directory, true); }
        CombinedDistance();
        FirstHeat();
        Console.WriteLine("Current heat rerun, scoring isolation, recovery and audit exports passed.");
    }

    private static void FirstHeat()
    {
        HeatRaceController heat = new(); LapRace race = new();
        heat.Configure(30, 10, new[] { "A", "B" }, 2);
        heat.Start(1000); Cross(heat, race, 1000); heat.Pause(7000);
        bool rejected = false;
        try { heat.RerunCurrentHeat(race.GetLaneSnapshots(), " "); } catch (ArgumentException) { rejected = true; }
        Check(rejected && heat.State == HeatRaceState.Paused, "blank reason leaves original state intact");
        heat.RerunCurrentHeat(race.GetLaneSnapshots(), "First heat");
        race.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts);
        Check(race.GetLapCounts().All(count => count == 0), "first heat restores zero totals");
        heat.Start(100000);
        Check(!heat.PrepareEdge(new(0, 1, 101000)).CountFirstEdgeAsLap, "first heat retains baseline crossing policy");
    }

    public static void CheckUi(string path)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using HeatRerunChoiceForm form = new();
                form.Show(); Application.DoEvents();
                var layout = (TableLayoutPanel)form.Controls[0];
                var buttons = (FlowLayoutPanel)layout.Controls[3];
                var confirm = (Button)buttons.Controls[1];
                Check(!confirm.Enabled && form.AcceptButton == null && form.CancelButton != null, "no default destructive acceptance");
                var reason = (TextBox)layout.Controls[2];
                reason.Text = "Controller interrupted during heat";
                Check(confirm.Enabled && form.Reason == reason.Text, "reason enables confirmation");
                using Bitmap bitmap = new(form.Width, form.Height);
                form.DrawToBitmap(bitmap, form.ClientRectangle with { Width = form.Width, Height = form.Height });
                bitmap.Save(path);
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }

    private static void CombinedDistance()
    {
        var qualifiers = Enumerable.Range(0, 4).Select(id => new QualifyingResult("R" + id, id, 5000)
            { Distance = DistanceScoring.Approve(10, 50, 70 - id, "qualifying") }).ToArray();
        HeatRaceController heat = new(); LapRace race = new();
        heat.Configure(30, 10, qualifiers.Select(item => item.RacerName).ToArray(), 2,
            qualifyingResults: qualifiers, format: RaceFormat.CombinedDistance,
            groups: new[] { new[] { "R0", "R1" }, new[] { "R2", "R3" } });
        for (int segment = 1; segment <= 4; segment++)
        {
            race.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts);
            uint start = (uint)(segment * 100000); heat.Start(start);
            if (heat.StartsAtLine) race.SeedStartLineTiming(heat.TimingBaseTimestamp);
            Cross(heat, race, start); heat.Complete(); heat.RecordHeatResults(race.GetLaneSnapshots());
            if (heat.IsGroupEnd)
                foreach (var candidate in heat.GetFinalDistanceCandidates()) heat.ApproveFinalDistance(candidate.RacerId, 37, "finish");
            if (segment < 4) Check(heat.PrepareNextHeat(race.GetLapCounts()), "advance distance group");
        }
        var before = heat.CaptureRecovery(0);
        heat.RerunCurrentHeat(race.GetLaneSnapshots(), "Finish uncertain");
        race.ResetTimingForHeat(heat.GetSnapshot(0).LaneLapCounts);
        var after = heat.CaptureRecovery(0);
        Check(after.Qualifiers.SequenceEqual(before.Qualifiers), "qualifying credit preserved");
        Check(after.Approvals.Count == 2 && after.Approvals.ContainsKey(0) && after.Approvals.ContainsKey(1), "earlier group approvals preserved");
        Check(after.Reruns![0].FinalDistanceApprovals.Count == 2, "voided finish approvals audited");
        Check(heat.GetRemaining(0).TotalSeconds == 30 && after.Lanes.SequenceEqual(before.Lanes), "distance start totals and lanes restored");
    }
}
