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

LapUpdate missed = race.Process(new LapEdge(0, 5, 6000));
Assert(missed.Kind == LapUpdateKind.MissedFrame, "sequence gap should be reported");
Assert(missed.MissedFrames == 1, "one missed frame should be counted");

LapUpdate stale = race.Process(new LapEdge(0, 4, 9000));
Assert(stale.Kind == LapUpdateKind.Duplicate, "stale sequence should be rejected");
Assert(race.GetLane(0).getCount() == 2, "stale sequence should not count a lap");

LapRace wrapRace = new(new LapRaceOptions(1000, 600000, 155.0));
Assert(wrapRace.Process(new LapEdge(0, uint.MaxValue, uint.MaxValue - 500)).Kind == LapUpdateKind.Started, "wrap race should start");
LapUpdate wrappedTime = wrapRace.Process(new LapEdge(0, 0, 1500));
Assert(wrappedTime.Kind == LapUpdateKind.Counted, "wrapped sequence and timestamp should count");
Assert(wrappedTime.LapMilliseconds == 2001, "timestamp wrap should preserve elapsed milliseconds");

Console.WriteLine("Protocol and lap race tests passed.");
