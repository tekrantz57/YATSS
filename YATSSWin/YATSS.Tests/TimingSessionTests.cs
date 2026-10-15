using YATSS;

internal static class TimingSessionTests
{
    private const string A = "0123456789ABCDEF", B = "FEDCBA9876543210";
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Timing session: " + message);
    }
    private static LapProtocolMessage Frame(string body, string id = A) =>
        LapProtocolParser.Parse(LapProtocolParser.EncodeFrame(body + ":" + id));
    private static LapProtocolMessage Hello(string id = A) =>
        Frame("HELLO:YATSSMC:5:8:ARDUINO_UNO_Q_STM32U585:test:2097152", id);

    internal static void Run()
    {
        Check(Hello().ControllerIdentity is { ProtocolVersion: 5, SessionId: A, FlashCapacityBytes: 2097152 }, "identity carries session and capacity");
        Check(Frame("EDGE:0:3:1200").Edge is { LaneIndex: 0, Sequence: 3, TimestampMillis: 1200 } &&
            Frame("EDGE:0:3:1200").SessionId == A, "edge retains lane, sequence, timestamp, and session");
        Check(Frame("HEARTBEAT:1300").ControllerTimestampMillis == 1300, "heartbeat timestamp parses");
        Check(Frame("HELLO:TRACK_POWER:OFF").Detail == "HELLO:TRACK_POWER:OFF", "power acknowledgement is session bound");
        Check(Frame("DIAG:STATUS:00:00:1800:0:1400").Diagnostic is ControllerDiagnosticStatus, "diagnostics carry session");
        Check(Frame("ERR:WINDOWS_WATCHDOG:1500").ControllerTimestampMillis == 1500, "watchdog timestamp remains available");
        foreach (string invalid in new[] { "", ":0000000000000000", ":123", ":0123456789ABCDEG", ":0123456789ABCDEF:extra" })
            Check(LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("HELLO:YATSSMC:5:8:BOARD:test:1234" + invalid)).Kind == LapProtocolMessageKind.Invalid,
                "malformed session identity must not become ready");
        Check(!LapProtocolParser.IsSessionId("0000000000000000"), "zero cannot be a session identifier");
        ControllerTimingSession tracking = new();
        Check(tracking.Observe(Frame("HEARTBEAT:1000")) == TimingSessionFrame.Unknown && tracking.Id == null,
            "heartbeat alone cannot establish a session");
        Check(tracking.Observe(Hello()) == TimingSessionFrame.Established, "identity establishes initial session");
        Check(tracking.Observe(Hello()) == TimingSessionFrame.Current, "PING reply must not signal restart");
        Check(tracking.Observe(LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("HEARTBEAT:1000"))) == TimingSessionFrame.Unsupported,
            "untagged physical timing traffic must be refused");

        long ticks = 0;
        ControllerSession readiness = new(() => ticks);
        readiness.BeginConnection(); readiness.ObserveIdentity(); readiness.ObservePowerOff();
        readiness.ObserveTimestamp(1000, heartbeat: true);
        Check(readiness.AuthorizeResume(), "verified controller can resume explicitly");
        HeatRaceController heat = new(); heat.Configure(60, 10, new[] { "A", "B" }, 2); heat.Start(1000);
        LapRace laps = new(new(100, 600000, 155));
        laps.Process(new(0, 1, 1000)); laps.Process(new(0, 2, 3000));
        readiness.ObserveTimestamp(3500, heartbeat: true);
        readiness.SuspendRace(heat, new QualifyingController(), laps);
        readiness.EndConnection(); readiness.BeginConnection();
        Check(tracking.Observe(Hello()) == TimingSessionFrame.Current, "reconnect preserves session history");
        Check(!readiness.IsReady && !readiness.PowerAllowed, "reconnect still needs fresh readiness and director authorization");
        Check(tracking.Observe(Frame("EDGE:0:1:1000", B)) == TimingSessionFrame.Unknown && tracking.Id == null,
            "unknown edge cannot silently replace the verified session");
        Check(tracking.Observe(Hello()) == TimingSessionFrame.Retired, "queued old identity cannot re-authorize after an unfamiliar frame");

        // A logical UNO Q reset can have a *larger* timestamp than the last old heartbeat.
        Check(tracking.Observe(Hello(B)) == TimingSessionFrame.Changed, "new session detects reset without clock rollback");
        readiness.RequireRecovery();
        readiness.ObserveTimestamp(200000, heartbeat: true, newClock: true);
        readiness.ObserveIdentity(); readiness.ObservePowerOff();
        Check(readiness.IsReady && !readiness.PowerAllowed && heat.State == HeatRaceState.Paused,
            "fresh session never resumes racing automatically");
        uint confirmed = readiness.LastConfirmedTimestamp;
        foreach (string body in new[] { "HEARTBEAT:300000", "EDGE:0:99:300001", "HELLO:TRACK_POWER:OFF",
                     "DIAG:STATUS:00:00:1800:0:300002", "ERR:WINDOWS_WATCHDOG:300003",
                     "HELLO:YATSSMC:5:8:ARDUINO_UNO_Q_STM32U585:test:2097152" })
            Check(tracking.Observe(Frame(body)) == TimingSessionFrame.Retired, "old-session traffic must be discarded before readiness or clock handling");
        Check(readiness.LastConfirmedTimestamp == confirmed && tracking.Id == B, "stale traffic cannot alter new clock or identity");
        readiness.AuthorizeResume(); heat.Resume(200000);
        LapUpdate crossing = laps.Process(heat.PrepareEdge(new(0, 1, 200500)).Edge);
        Check(crossing.Kind == LapUpdateKind.Counted && crossing.LapMilliseconds == null && !crossing.FastestLapEligible && crossing.MissedFrames == 0,
            "first new-session crossing keeps totals but cannot create a fast lap or sequence gap");
        Check(heat.GetRemaining(200500) == TimeSpan.FromMilliseconds(57000), "session reset preserves consumed race time");
        tracking.RetireCurrent();
        Check(tracking.Observe(Hello(B)) == TimingSessionFrame.Retired, "explicit reset retires its session before a new identity arrives");
        Check(tracking.Observe(Hello("1234567890ABCDEF")) == TimingSessionFrame.Changed, "new reset identity can establish readiness again");
        foreach (string body in new[] { "PING", "KEEPALIVE", "TRACK_POWER:OFF", "TRACK_POWER:MASK:00" })
            Check(Serial.EncodeControllerCommand(body, A) == LapProtocolParser.EncodeFrame(body), "discovery and emergency off remain unbound");
        Check(Serial.EncodeControllerCommand("TRACK_POWER:ON", A) == LapProtocolParser.EncodeFrame("TRACK_POWER:ON:" + A),
            "power enable command is bound to the current session");
        Check(Serial.EncodeControllerCommand("DIAG:RELAY:PULSE:0:200", A).Contains(":" + A + "*"), "diagnostic pulse is session bound");
        Console.WriteLine("Controller timing-session protocol and recovery tests passed.");
    }
}
