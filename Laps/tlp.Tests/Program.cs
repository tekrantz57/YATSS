using tlp;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

string encodedEdge = LapProtocolParser.EncodeFrame("EDGE:3:42:12345");
LapProtocolMessage edge = LapProtocolParser.Parse(encodedEdge);
Assert(edge.Kind == LapProtocolMessageKind.Edge, "checksummed EDGE should parse");
Assert(edge.Edge is { LaneIndex: 3, Sequence: 42, TimestampMillis: 12345 }, "EDGE fields should parse");

LapProtocolMessage corrupt = LapProtocolParser.Parse("EDGE:3:42:12345*00");
Assert(corrupt.Kind == LapProtocolMessageKind.Invalid, "bad checksum should be rejected");

LapProtocolMessage boot = LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("HELLO:LAPS_REDUX:2:8"));
Assert(boot.Kind == LapProtocolMessageKind.Hello, "HELLO should parse");

LapProtocolMessage heartbeat = LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("HEARTBEAT:12345"));
Assert(heartbeat.Kind == LapProtocolMessageKind.Heartbeat, "HEARTBEAT should parse");
Assert(heartbeat.ControllerTimestampMillis == 12345, "HEARTBEAT timestamp should parse");

LapProtocolMessage badLane = LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("EDGE:8:1:100"));
Assert(badLane.Kind == LapProtocolMessageKind.Invalid, "Lane 8 should be rejected");

LapProtocolMessage legacy = LapProtocolParser.Parse("02:0000010000");
Assert(legacy.Kind == LapProtocolMessageKind.Edge, "legacy two-part edge should parse");
Assert(legacy.Edge is { LaneIndex: 2, TimestampMillis: 10000 }, "legacy fields should parse");

LapProtocolMessage oldOneBased = LapProtocolParser.Parse("1:7:0000012500");
Assert(oldOneBased.Kind == LapProtocolMessageKind.Edge, "old three-part edge should parse");
Assert(oldOneBased.Edge is { LaneIndex: 0, TimestampMillis: 12500 }, "old three-part lane should be one-based");

LapRace race = new(new LapRaceOptions(1000, 600000, 155.0));
LapUpdate started = race.Process(new LapEdge(0, 1, 1000));
Assert(started.Kind == LapUpdateKind.Started, "first edge should establish baseline");

LapUpdate duplicate = race.Process(new LapEdge(0, 2, 1500));
Assert(duplicate.Kind == LapUpdateKind.TooFast, "short edge should be rejected as too fast");
Assert(duplicate.LapMilliseconds == 500, "too-fast update should report ignored lap duration");

LapUpdate counted = race.Process(new LapEdge(0, 3, 3500));
Assert(counted.Kind == LapUpdateKind.Counted, "valid edge should count lap");
Assert(counted.LapMilliseconds == 2500, "lap duration should be computed from last accepted edge");
Assert(race.GetLane(0).getCount() == 1, "lane should have one counted lap");
Assert(race.AdjustLapCount(0, 1) == 2, "manual add should increase lap count");
Assert(race.AdjustLapCount(0, -1) == 1, "manual subtract should decrease lap count");
Assert(race.AdjustLapCount(0, -5) == 0, "manual subtract should not create negative lap count");

race.ResetLane(0);
Assert(race.GetLane(0).getCount() == 0, "lane reset should clear lap count");
Assert(race.Process(new LapEdge(0, 4, 5000)).Kind == LapUpdateKind.Started, "lane reset should establish a fresh baseline");

LapUpdate missed = race.Process(new LapEdge(0, 5, 6000));
Assert(missed.Kind == LapUpdateKind.Counted, "post-reset next valid edge should count without a sequence gap");

LapUpdate stale = race.Process(new LapEdge(0, 4, 9000));
Assert(stale.Kind == LapUpdateKind.Duplicate, "stale sequence should be rejected");
Assert(race.GetLane(0).getCount() == 1, "stale sequence should not count a lap");

LapRace ineligibleBestRace = new(new LapRaceOptions(1000, 600000, 155.0));
Assert(ineligibleBestRace.Process(new LapEdge(0, 1, 1000)).Kind == LapUpdateKind.Started, "ineligible best race should start");
LapUpdate ineligibleBest = ineligibleBestRace.Process(new LapEdge(0, 2, 3000), countFirstEdgeAsLap: false, fastestLapEligible: false);
Assert(ineligibleBest.Kind == LapUpdateKind.Counted, "ineligible best lap should still count");
Assert(ineligibleBestRace.GetLane(0).getCount() == 1, "ineligible best lap should increment count");
Assert(ineligibleBestRace.GetLane(0).best_time == int.MaxValue, "ineligible best lap should not set best time");

LapRace carriedCountRace = new(new LapRaceOptions(1000, 600000, 155.0));
carriedCountRace.ResetTimingForHeat(new[] { 4, 0, 0, 0, 0, 0, 0, 0 });
LapUpdate countedFirstEdge = carriedCountRace.Process(new LapEdge(0, 1, 1000), countFirstEdgeAsLap: true, fastestLapEligible: false);
Assert(countedFirstEdge.Kind == LapUpdateKind.Counted, "first edge can count in successive heats");
Assert(!countedFirstEdge.LapMilliseconds.HasValue, "first counted edge should not have a timed lap");
Assert(carriedCountRace.GetLane(0).getCount() == 5, "first counted edge should increment carried count");
Assert(carriedCountRace.GetLane(0).getMedian() == 0, "first counted edge should not affect timing samples");

LapRace timedFirstLapRace = new(new LapRaceOptions(1000, 600000, 155.0));
timedFirstLapRace.ResetTimingForHeat(new[] { 4, 0, 0, 0, 0, 0, 0, 0 });
LapUpdate timedFirstLap = timedFirstLapRace.Process(
    new LapEdge(0, 1, 61000),
    countFirstEdgeAsLap: true,
    fastestLapEligible: false,
    firstLapMilliseconds: 1000);
Assert(timedFirstLap.LapMilliseconds == 1000, "successive heat first lap should report heat-start timing");
Assert(timedFirstLapRace.GetLane(0).getCount() == 5, "timed first lap should increment carried count");
Assert(timedFirstLapRace.GetLane(0).getMedian() == 1000, "timed first lap should contribute to median");
Assert(timedFirstLapRace.GetLane(0).best_time == int.MaxValue, "timed first lap should be excluded from fastest lap");

LapRace wrapRace = new(new LapRaceOptions(1000, 600000, 155.0));
Assert(wrapRace.Process(new LapEdge(0, uint.MaxValue, uint.MaxValue - 500)).Kind == LapUpdateKind.Started, "wrap race should start");
LapUpdate wrappedTime = wrapRace.Process(new LapEdge(0, 0, 1500));
Assert(wrappedTime.Kind == LapUpdateKind.Counted, "wrapped sequence and timestamp should count");
Assert(wrappedTime.LapMilliseconds == 2001, "timestamp wrap should preserve elapsed milliseconds");

HeatRaceController heat = new();
string[] racers = { "R0", "R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8" };
heat.Configure(1, 15, racers);
Assert(heat.State == HeatRaceState.Ready, "configured heat should be ready");
Assert(heat.BetweenHeatsSeconds == 15, "between-heats seconds should be configured");
Assert(heat.HeatNumber == 1, "configured heat should start at heat 1");
HeatRaceSnapshot firstHeat = heat.GetSnapshot(0);
Assert(firstHeat.LaneRacers[0] == "R0", "first heat should assign first racer to red");
Assert(firstHeat.LaneRacers[1] == "R1", "first heat should assign second racer to white");
Assert(firstHeat.LaneRacers[2] == "R2", "first heat should assign third racer to green");
Assert(firstHeat.LaneRacers[3] == "R3", "first heat should assign fourth racer to orange");
Assert(firstHeat.LaneRacers[4] == "R4", "first heat should assign fifth racer to blue");
Assert(firstHeat.LaneRacers[5] == "R5", "first heat should assign sixth racer to yellow");
Assert(firstHeat.LaneRacers[6] == "R6", "first heat should assign seventh racer to purple");
Assert(firstHeat.LaneRacers[7] == "R7", "first heat should assign eighth racer to black");
Assert(firstHeat.OnDeckRacer == "R8", "ninth racer should be on deck");
Assert(heat.Start(1000), "ready heat should start");
Assert(heat.PrepareEdge(new LapEdge(0, 1, 2000)).Edge.TimestampMillis == 1000, "running heat should adjust edge to active time");
Assert(heat.Pause(10000), "running heat should pause");
Assert(heat.Resume(20000), "paused heat should resume");
HeatRaceEdgeDecision adjustedAfterPause = heat.PrepareEdge(new LapEdge(0, 2, 21000));
Assert(adjustedAfterPause.Edge.TimestampMillis == 10000, "heat adjustment should subtract paused time");
Assert(!heat.IsExpired(70999), "heat should not expire before configured active time");
Assert(heat.IsExpired(71000), "heat should expire at configured active time");
Assert(heat.Complete(), "expired heat should complete");
Assert(!heat.PrepareEdge(new LapEdge(0, 9, 72000)).ShouldProcess, "edges between heats should be ignored");
Assert(heat.PrepareNextHeat(new[] { 10, 11, 12, 13, 14, 15, 16, 17 }), "completed heat should prepare next heat");
Assert(heat.HeatNumber == 2, "next heat should be heat 2");
HeatRaceSnapshot secondHeat = heat.GetSnapshot(80000);
Assert(secondHeat.LaneRacers[0] == "R8", "waiting racer should enter on red");
Assert(secondHeat.LaneRacers[2] == "R0", "red racer should rotate to green");
Assert(secondHeat.LaneRacers[4] == "R2", "green racer should rotate to blue");
Assert(secondHeat.LaneRacers[6] == "R4", "blue racer should rotate to purple");
Assert(secondHeat.LaneRacers[7] == "R6", "purple racer should rotate to black");
Assert(secondHeat.LaneRacers[5] == "R7", "black racer should rotate to yellow");
Assert(secondHeat.LaneRacers[3] == "R5", "yellow racer should rotate to orange");
Assert(secondHeat.LaneRacers[1] == "R3", "orange racer should rotate to white");
Assert(secondHeat.OnDeckRacer == "R1", "white racer should rotate out to on deck");
Assert(secondHeat.LaneLapCounts[2] == 10, "red racer's lap total should rotate to green");
Assert(secondHeat.LaneLapCounts[1] == 13, "orange racer's lap total should rotate to white");

HeatRaceController shortFieldHeat = new();
shortFieldHeat.Configure(1, 0, new[] { "A", "B", "C" });
HeatRaceSnapshot shortField = shortFieldHeat.GetSnapshot(0);
Assert(shortField.LaneRacers[0] == "A", "three racers should start red first");
Assert(shortField.LaneRacers[1] == "B", "three racers should start white second");
Assert(shortField.LaneRacers[2] == "C", "three racers should start green third");
Assert(shortFieldHeat.GetOccupiedLaneMask() == 0x07, "short field should power only occupied lanes");
Assert(shortFieldHeat.Start(100), "short field heat should start");
Assert(shortFieldHeat.PrepareEdge(new LapEdge(4, 1, 200)).ShouldProcess == false, "unoccupied heat lane should be ignored");
Assert(shortFieldHeat.PrepareEdge(new LapEdge(0, 1, 200)).ShouldProcess, "occupied heat lane should process");
Assert(shortFieldHeat.Complete(), "short field heat should complete");
Assert(shortFieldHeat.PrepareNextHeat(new[] { 1, 2, 3, 0, 0, 0, 0, 0 }), "short field should prepare its next heat");
Assert(shortFieldHeat.GetOccupiedLaneMask() == 0x15, "powered lanes should follow racer rotation");
Assert(heat.Start(80000), "next heat should start");
HeatRaceEdgeDecision secondHeatFirstEdge = heat.PrepareEdge(new LapEdge(0, 3, 81000));
Assert(secondHeatFirstEdge.Edge.TimestampMillis == 61000, "second heat adjusted time should continue after heat 1");
Assert(secondHeatFirstEdge.CountFirstEdgeAsLap, "first lane edge after heat 1 should count as a lap");
Assert(!secondHeatFirstEdge.FastestLapEligible, "first lane edge after heat 1 should not be fastest eligible");
Assert(secondHeatFirstEdge.FirstLapMilliseconds == 1000, "successive heat first lap should use active time since heat start");

HeatRaceController fourLaneHeat = new();
fourLaneHeat.Configure(1, 0, new[] { "A", "B", "C", "D", "E" }, activeLaneCount: 4);
Assert(fourLaneHeat.TotalHeats == 4, "four-lane race should run four heats");
HeatRaceSnapshot fourLaneFirstHeat = fourLaneHeat.GetSnapshot(0);
Assert(fourLaneFirstHeat.LaneRacers.Take(4).SequenceEqual(new[] { "A", "B", "C", "D" }), "four-lane first heat should fill its four physical lanes");
Assert(fourLaneHeat.GetOccupiedLaneMask() == 0x0F, "four-lane heat should power only its four physical lanes");
Assert(fourLaneFirstHeat.OnDeckRacer == "E", "fifth racer should wait in a four-lane race");
Assert(fourLaneHeat.Start(0), "four-lane first heat should start");
Assert(fourLaneHeat.Complete(), "four-lane first heat should complete");
Assert(fourLaneHeat.PrepareNextHeat(new[] { 1, 2, 3, 4, 0, 0, 0, 0 }), "four-lane second heat should prepare");
HeatRaceSnapshot fourLaneSecondHeat = fourLaneHeat.GetSnapshot(0);
Assert(fourLaneSecondHeat.LaneRacers[0] == "E", "waiting racer should enter on red");
Assert(fourLaneSecondHeat.LaneRacers[2] == "A", "red racer should rotate to green");
Assert(fourLaneSecondHeat.LaneRacers[3] == "C", "green racer should rotate to orange");
Assert(fourLaneSecondHeat.LaneRacers[1] == "D", "orange racer should rotate to white");
Assert(fourLaneSecondHeat.OnDeckRacer == "B", "white racer should rotate out");

LaneConfiguration[] customLanes = LaneConfiguration.CreateDefaults().ToArray();
customLanes[0] = new LaneConfiguration("Aqua", System.Drawing.Color.Aqua.ToArgb());
customLanes[1] = new LaneConfiguration("Pink", System.Drawing.Color.HotPink.ToArgb());
HeatRaceController customLaneHeat = new();
customLaneHeat.Configure(1, 0, new[] { "A", "B" }, activeLaneCount: 4, laneConfigurations: customLanes);
HeatRaceReport customLaneReport = customLaneHeat.CreateReport();
Assert(customLaneReport.LaneNames[0] == "Aqua", "report should use configured lane names");
Assert(customLaneReport.LaneColorArgb[1] == System.Drawing.Color.HotPink.ToArgb(), "report should use configured lane colors");

HeatRaceController reportHeat = new();
reportHeat.Configure(1, 0, new[] { "Ada", "Grace" });
Assert(reportHeat.Start(0), "report heat should start");
Assert(reportHeat.Pause(1000), "paused heat should allow lap adjustment");
Assert(reportHeat.CanAdjustLapCounts, "paused heat should allow manual lap adjustment");
reportHeat.RecordHeatResults(
    new[] { 5, 4, 0, 0, 0, 0, 0, 0 },
    new int?[] { 2100, 2200, null, null, null, null, null, null });
HeatRaceReport report = reportHeat.CreateReport();
Assert(report.HeatLengthMinutes == 1, "report should include heat length");
Assert(report.BetweenHeatsSeconds == 0, "report should include between-heat seconds");
Assert(report.Notes.Contains("Manual lap adjustments", StringComparison.OrdinalIgnoreCase), "report should include manual adjustment note");
Assert(report.Racers[0].RacerName == "Ada", "report should sort finish order by total laps");
Assert(report.Racers[0].TotalLaps == 5, "report should include total laps");
Assert(report.Racers[0].HeatLaps[0] == 5, "report should include heat laps");
Assert(report.Racers[0].BestLapByLaneMilliseconds[0] == 2100, "report should include fast lap by lane");

Console.WriteLine("Protocol and lap race tests passed.");
