using System.Text.Json;
using YATSS;

internal static class DistanceTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Combined Distance: " + message);
    }

    public static void Run()
    {
        Check(DistanceScoring.Format(1070) == "10.70", "hundredths formatting");
        Check(DistanceScoring.EstimatePartial(2999, new[] { 5000 }) == 59, "estimates round down");
        Check(DistanceScoring.EstimatePartial(10000, new[] { 5000 }) == 99, "estimates never add a lap");
        Check(DistanceScoring.EstimatePartial(2000, Array.Empty<int>()) == null, "unknown without valid pace");
        bool rejected = false;
        try { DistanceScoring.Approve(10, 70, 100, "track"); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "reject full-lap fractions");
        LapRace startLine = new();
        startLine.SeedStartLineTiming(1000);
        Check(startLine.Process(new LapEdge(0, 1, 1001)).Kind == LapUpdateKind.TooFast, "departure pulse is not a lap");
        Check(startLine.Process(new LapEdge(0, 2, 6000)).Kind == LapUpdateKind.Counted && startLine.GetLapCounts()[0] == 1,
            "first full lap from the starting line must count");

        QualifyingController qualifier = new();
        qualifier.Configure(new[] { "A", "B" }, 0, 60);
        qualifier.Start(1000);
        qualifier.Pause(2000);
        qualifier.Resume(4000);
        qualifier.CompleteCurrent(new[] { new LaneLapRecord(5000, true, 8000) }, 64000);
        rejected = false;
        try { qualifier.GetDistanceResults(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "unapproved qualifying cannot seed a race");
        qualifier.ApproveLastDistance(70, "section chart");
        qualifier.Start(70000);
        qualifier.CompleteCurrent(new[] { new LaneLapRecord(4000, true, 74000) }, 130000);
        qualifier.ApproveLastDistance(70, "section chart");
        Check(qualifier.GetDistanceResults()[0].RacerName == "B", "equal distance uses valid best lap");
        Check(qualifier.GetDistanceResults()[0].Distance!.TotalHundredths == 170, "approved distance");
        var groupQualifiers = Enumerable.Range(0, 10).Select(id => new QualifyingResult($"G{id}", id, 5000)).ToArray();
        var selectedGroups = QualifyingController.BuildGroups(groupQualifiers, Enumerable.Range(0, 10).Select(id => id % 8).ToArray(), 8);
        Check(selectedGroups.Count == 2 && selectedGroups[0][0] == "G0" && selectedGroups[1][0] == "G8" && selectedGroups[1][2] == "",
            "ranked groups independently reuse lane choices and preserve short groups");
        rejected = false;
        try { QualifyingController.BuildGroups(groupQualifiers, new int[10], 8); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "duplicate lane within a group rejected");

        foreach (int racerCount in new[] { 2, 8, 10, 17 }) RunRace(racerCount, 8);
        RunRace(5, 4);
        RunRace(2, 2, exactTie: true);
        Console.WriteLine("Combined Distance scoring, group rotation, approvals, projections, and exports passed.");
    }

    private static void RunRace(int racerCount, int laneCount, bool exactTie = false)
    {
        QualifyingResult[] qualifiers = Enumerable.Range(0, racerCount).Select(id => new QualifyingResult($"Racer {id}", id, 5000)
        {
            Distance = DistanceScoring.Approve(10, 50, exactTie ? 70 : 70 - id, "qualifying section"),
            DirectorTieOrder = id
        }).ToArray();
        List<IReadOnlyList<string>> groups = new();
        for (int offset = 0; offset < racerCount; offset += laneCount)
        {
            string[] lanes = Enumerable.Repeat(string.Empty, laneCount).ToArray();
            for (int index = 0; index < Math.Min(laneCount, racerCount - offset); index++)
                lanes[(index + 2) % laneCount] = qualifiers[offset + index].RacerName;
            groups.Add(lanes);
        }
        HeatRaceController heat = new();
        LapRace race = new();
        heat.Configure(1, 5, qualifiers.Select(result => result.RacerName).ToArray(), laneCount,
            raceName: "Distance <test>", qualifyingResults: qualifiers, format: RaceFormat.CombinedDistance, groups: groups);
        Check(heat.TotalHeats == groups.Count * laneCount, "each group has a full lane rotation");
        Dictionary<string, HashSet<int>> visited = qualifiers.ToDictionary(result => result.RacerName, _ => new HashSet<int>());
        uint start = 1000;
        for (int segment = 1; segment <= heat.TotalHeats; segment++)
        {
            HeatRaceSnapshot snapshot = heat.GetSnapshot(start);
            race.ResetTimingForHeat(snapshot.LaneLapCounts);
            Check(heat.Start(start), "start segment");
            if (heat.StartsAtLine) race.SeedStartLineTiming(heat.TimingBaseTimestamp);
            for (int lane = 0; lane < laneCount; lane++)
            {
                if (string.IsNullOrWhiteSpace(snapshot.LaneRacers[lane])) continue;
                Check(visited[snapshot.LaneRacers[lane]].Add(lane), "racer must not repeat a lane");
                for (uint crossing = 0; crossing < 3; crossing++)
                {
                    HeatRaceEdgeDecision decision = heat.PrepareEdge(new LapEdge(lane, crossing + 1, start + 100 + crossing * 5000));
                    Check(decision.CountFirstEdgeAsLap == (crossing == 0 && (segment - 1) % laneCount != 0), "first-group crossing establishes baseline; rotations count it");
                    race.Process(decision.Edge, decision.CountFirstEdgeAsLap, decision.FastestLapEligible, decision.FirstLapMilliseconds);
                }
            }
            var live = heat.GetDistanceStandings(start + 20000, race.GetLaneSnapshots());
            Check(live.All(row => row.CombinedHundredths == row.QualifyingHundredths + row.RaceLaps * 100L + (row.FinalPartialHundredths ?? 0)), "official totals exclude unapproved estimates");
            Check(heat.Pause(start + 20000), "track call");
            var paused = heat.GetDistanceStandings(start + 30000, race.GetLaneSnapshots());
            var stillPaused = heat.GetDistanceStandings(start + 40000, race.GetLaneSnapshots());
            Check(paused.SequenceEqual(stillPaused), "projections freeze during track calls");
            Check(heat.Resume(start + 40000), "resume track call");
            Check(heat.Complete(), "complete segment");
            heat.RecordHeatResults(race.GetLaneSnapshots());
            if (heat.IsGroupEnd)
            {
                if (heat.HasMoreHeats) Check(!heat.PrepareNextHeat(race.GetLapCounts()), "unconfirmed fractions block group transition");
                bool unconfirmedRejected = false;
                try { heat.CreateReport(); } catch (InvalidOperationException) { unconfirmedRejected = true; }
                Check(unconfirmedRejected, "unconfirmed fractions block official reports");
                foreach (var candidate in heat.GetFinalDistanceCandidates()) heat.ApproveFinalDistance(candidate.RacerId, 37, "finish section");
                Check(heat.GetFinalDistanceCandidates().Count == 0, "explicit confirmation includes every racer");
            }
            if (heat.HasMoreHeats) Check(heat.PrepareNextHeat(race.GetLapCounts()), "next segment");
            start += 100000;
        }
        Check(visited.All(pair => pair.Value.Count == laneCount), "every racer drives every lane");
        Check(heat.FinalDistancesConfirmed, "all fractions confirmed");
        if (exactTie)
        {
            bool unresolvedRejected = false;
            try { heat.CreateReport(); } catch (InvalidOperationException) { unresolvedRejected = true; }
            Check(unresolvedRejected, "exact ties block finalization");
            heat.SetFinalTieOrder(qualifiers.Select(result => result.OriginalOrder).Reverse().ToArray());
        }
        HeatRaceReport report = heat.CreateReport();
        Check(report.DistanceStandings.Count == racerCount, "every racer in official standings");
        Check(report.DistanceStandings.All(row => row.RaceLaps == laneCount * 3 - 1 && row.FinalPartialHundredths == 37), "no partial lap duplication");
        Check(report.DistanceStandings[0].RacerId == (exactTie ? racerCount - 1 : 0), "qualifying credit and director tie order affect final order");
        if (exactTie) Check(report.FinalTieOrderRecordedAt != null, "tie decision has an audit timestamp");
        Check(report.FinalDistanceApprovals.Count == racerCount, "audit covers all racers");

        string directory = Path.Combine(Path.GetTempPath(), "YATSS.Tests", "distance-" + Guid.NewGuid());
        try
        {
            RaceExportPaths paths = RaceArchiveWriter.Write(report, directory);
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(paths.Json!));
            Check(json.RootElement.GetProperty("race").GetProperty("distanceStandings").GetArrayLength() == racerCount, "JSON includes standings");
            Check(File.ReadAllText(paths.Html).Contains("Official Combined Distance"), "HTML distance results");
            Check(File.ReadAllText(paths.Html).Contains("Distance &lt;test&gt;"), "HTML escapes names");
            Check(Directory.GetFiles(directory, "*_distance.csv").Length == 1, "distance CSV");
            Check(File.ReadAllLines(Directory.GetFiles(directory, "*_distance_confirmations.csv").Single()).Length == racerCount * 2 + 1, "CSV confirmation audit");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
