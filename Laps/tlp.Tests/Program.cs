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
LapUpdate ineligibleBest = ineligibleBestRace.Process(new LapEdge(0, 2, 3000), fastestLapEligible: false);
Assert(ineligibleBest.Kind == LapUpdateKind.Counted, "ineligible best lap should still count");
Assert(ineligibleBestRace.GetLane(0).getCount() == 1, "ineligible best lap should increment count");
Assert(ineligibleBestRace.GetLane(0).best_time == int.MaxValue, "ineligible best lap should not set best time");

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
Assert(firstHeat.LaneRacers[0] == "R0" && firstHeat.LaneRacers[7] == "R7", "first heat should assign first eight racers to lanes");
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
Assert(heat.PrepareNextHeat(), "completed heat should prepare next heat");
Assert(heat.HeatNumber == 2, "next heat should be heat 2");
HeatRaceSnapshot secondHeat = heat.GetSnapshot(80000);
Assert(secondHeat.LaneRacers[0] == "R8", "waiting racer should enter on red");
Assert(secondHeat.LaneRacers[1] == "R0", "red racer should rotate to green");
Assert(secondHeat.LaneRacers[7] == "R6", "orange racer should rotate to white");
Assert(secondHeat.OnDeckRacer == "R7", "white racer should rotate out to on deck");
Assert(heat.Start(80000), "next heat should start");
HeatRaceEdgeDecision secondHeatFirstEdge = heat.PrepareEdge(new LapEdge(0, 3, 81000));
Assert(secondHeatFirstEdge.Edge.TimestampMillis == 61000, "second heat adjusted time should continue after heat 1");
Assert(!secondHeatFirstEdge.FastestLapEligible, "first lane edge after heat 1 should not be fastest eligible");

Console.WriteLine("Protocol and lap race tests passed.");
