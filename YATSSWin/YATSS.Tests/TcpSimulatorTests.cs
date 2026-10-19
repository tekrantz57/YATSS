using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using YATSS;
using YATSS.Simulation;

internal static class TcpSimulatorTests
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("TCP simulator: " + message); }

    internal static void Run()
    {
        QualifyingController qualifying = new();
        qualifying.Configure(new[] { "Clock fixture" }, 0, 3);
        qualifying.Start(2000);
        Check(!qualifying.IsExpired(1999) && qualifying.GetRemaining(1999) == TimeSpan.FromSeconds(3),
            "pre-start confirmed heartbeat must not instantly expire qualifying");
        Check(qualifying.IsExpired(5000), "qualifying still expires at its actual duration");
        qualifying.Reset(); qualifying.Configure(new[] { "Wrap fixture" }, 0, 3);
        qualifying.Start(uint.MaxValue - 500);
        Check(!qualifying.IsExpired(499) && qualifying.GetRemaining(499) == TimeSpan.FromSeconds(2),
            "normal timestamp wrap retains qualifying elapsed time");
        TransportChecks().GetAwaiter().GetResult();
        AppChecks();
        Console.WriteLine("Loopback TCP simulator and real-app fault/recovery integration tests passed.");
    }

    private static async Task TransportChecks()
    {
        await using TcpControllerSimulator simulator = new(0);
        using StringWriter console = new();
        Check(SimulatorConsole.Execute(simulator, "edge 9", console) && console.ToString().Length > 0,
            "invalid console input is reported without terminating the server");
        Check(!SimulatorConsole.Execute(simulator, "quit", console), "console quit ends the session");
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, simulator.Port);
        using StreamReader input = new(client.GetStream(), Encoding.ASCII);
        using StreamWriter output = new(client.GetStream(), Encoding.ASCII) { AutoFlush = true };
        async Task<LapProtocolMessage> Until(Func<LapProtocolMessage, bool> predicate)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var message = LapProtocolParser.Parse(await input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)));
                if (predicate(message)) return message;
            }
            throw new Exception("Expected simulator response was not received");
        }
        var identity = await Until(message => message.ControllerIdentity != null);
        Check(identity.ControllerIdentity!.BoardProfile == TcpControllerSimulator.BoardProfile, "simulator must identify itself, never impersonate a hardware board");
        await output.WriteLineAsync(LapProtocolParser.EncodeFrame("TRACK_POWER:ON"));
        await Until(message => message.Detail == "ERR:STALE_SESSION");
        Check(simulator.Status.PowerMask == 0, "unbound power command rejected");
        await output.WriteLineAsync(LapProtocolParser.EncodeFrame("TRACK_POWER:ON:" + identity.SessionId));
        await Until(message => message.Detail == "HELLO:TRACK_POWER:ON");
        Check(simulator.Status.PowerMask == 255, "session-bound command changes virtual mask");
        using TcpClient second = new();
        await second.ConnectAsync(IPAddress.Loopback, simulator.Port);
        Check(await second.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)) == 0,
            "second client cannot replace the active app connection");
        simulator.ResetController(preserveClock: true);
        var reset = await Until(message => message.ControllerIdentity != null);
        Check(reset.SessionId != identity.SessionId && simulator.Status.PowerMask == 0, "reset rotates identity and virtual power goes off");
        await output.WriteLineAsync(LapProtocolParser.EncodeFrame("TRACK_POWER:ON:" + identity.SessionId));
        await Until(message => message.Detail == "ERR:STALE_SESSION");
        simulator.SendInvalidChecksum();
        await Until(message => message.Kind == LapProtocolMessageKind.Invalid);
    }

    internal static void RunUi()
    {
        AppDatabase.Open();
        var simulator = new TcpControllerSimulator(0, Console.WriteLine);
        AppDatabase.SaveSerialPort($"TCP:127.0.0.1:{simulator.Port}");
        AppDatabase.SaveAppSettings(new AppSettings(100, false, false, false, "", SpeechBackendMode.None, 8));
        string[] racers = Enumerable.Range(1, 8).Select(index => $"Test Racer {index}").ToArray();
        AppDatabase.SaveRacerNames(racers);
        Console.WriteLine($"Isolated TCP test window. Data and logs: {TestWorkspace.Root}");
        Exception? failure = null;
        Thread ui = new(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var form = new global::YATSS.YATSS();
                var serial = (Serial)typeof(global::YATSS.YATSS).GetField("s", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                form.Shown += (_, _) =>
                {
                    serial.ConfigureHeatRace("TCP test race", 60, 60, racers, 8, LaneConfiguration.CreateDefaults(), 155);
                    Thread console = new(() =>
                    {
                        SimulatorConsole.Run(simulator);
                        try { form.BeginInvoke(new Action(form.Close)); }
                        catch (InvalidOperationException) { }
                    }) { IsBackground = true };
                    console.Start();
                };
                Application.Run(form);
            }
            catch (Exception ex) { failure = ex; }
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        try { ui.Join(); }
        finally { simulator.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        if (failure != null) throw new InvalidOperationException("TCP test window failed", failure);
    }

    private static void AppChecks()
    {
        bool databaseWasOpen = AppDatabase.Connection.State == System.Data.ConnectionState.Open;
        AppDatabase.Open();
        string oldPort = AppDatabase.LoadSerialPort();
        var fallback = new AppSettings(100, false, false, false, "", SpeechBackendMode.None, 2);
        AppSettings oldSettings = AppDatabase.LoadAppSettings(fallback);
        Exception? failure = null;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(100));
        var simulator = new TcpControllerSimulator(0);
        AppDatabase.SaveSerialPort($"TCP:127.0.0.1:{simulator.Port}");
        AppDatabase.SaveAppSettings(fallback);
        Thread thread = new(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var form = new global::YATSS.YATSS();
                var serial = (Serial)typeof(global::YATSS.YATSS).GetField("s", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                form.Shown += async (_, _) =>
                {
                    try { await ExerciseApp(form, serial, simulator, timeout.Token); }
                    catch (Exception ex) { failure = ex; }
                    finally { serial.SetPracticeMode(); form.Close(); }
                };
                Application.Run(form);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        try
        {
            Check(thread.Join(TimeSpan.FromSeconds(115)), "actual-app scenario timed out");
            if (failure != null) throw new InvalidOperationException("Actual-app TCP integration failed", failure);
        }
        finally
        {
            simulator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            AppDatabase.SaveSerialPort(oldPort); AppDatabase.SaveAppSettings(oldSettings);
            if (!databaseWasOpen) AppDatabase.Connection.Close();
        }
    }

    private static async Task ExerciseApp(Form form, Serial serial, TcpControllerSimulator simulator, CancellationToken cancellation)
    {
        async Task Wait(Func<ControllerTestStatus, bool> predicate, string step)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                cancellation.ThrowIfCancellationRequested();
                if (predicate(serial.CaptureTestStatus())) return;
                await Task.Delay(50, cancellation);
            }
            var status = serial.CaptureTestStatus();
            throw new Exception(step + ": " + System.Text.Json.JsonSerializer.Serialize(status) +
                "; simulator=" + System.Text.Json.JsonSerializer.Serialize(simulator.Status));
        }
        async Task StartHeat()
        {
            serial.HandleSpaceBar();
            await Wait(status => status.HeatState == HeatRaceState.Running && !status.Countdown && simulator.Status.PowerMask != 0,
                "director start/resume");
        }
        await Wait(status => status.Ready, "initial identity/heartbeat/off handshake");
        Check(!serial.DemoLapStreamActive && simulator.Status.PowerMask == 0, "real TCP path, not internal demo input");
        Check(!serial.CanUpdateControllerFirmware(out _), "simulator cannot enter firmware flashing");
        serial.ConfigureHeatRace("TCP fault fixture", 60, 60, new[] { "Fixture A", "Fixture B" }, 2, LaneConfiguration.CreateDefaults(), 155);
        await StartHeat();
        simulator.EmitEdge(0); await Task.Delay(100, cancellation);
        simulator.AdvanceClock(2000); simulator.EmitEdge(0);
        await Wait(status => status.Laps[0] >= 1 && status.BestLaps[0] < int.MaxValue, "crossings reach actual app scoring");
        var scored = serial.CaptureTestStatus();
        string session = simulator.Status.Session;

        simulator.SetOnline(false);
        await Wait(status => status.HeatState == HeatRaceState.Paused && status.NeedsResume, "transport loss pauses race");
        TimeSpan stopped = serial.CaptureTestStatus().Remaining;
        await Task.Delay(500, cancellation);
        Check(serial.CaptureTestStatus().Remaining == stopped, "remaining time is frozen during outage");
        simulator.SetOnline(true);
        await Wait(status => status.Ready, "same-session TCP reconnect");
        Check(serial.CaptureTestStatus().Session == session && serial.CaptureTestStatus().HeatState == HeatRaceState.Paused,
            "same-session reconnect preserves identity and does not resume automatically");
        simulator.EmitEdge(0); await Task.Delay(100, cancellation);
        Check(serial.CaptureTestStatus().Laps[0] == scored.Laps[0], "crossings before explicit resume are ignored");
        await StartHeat();
        simulator.AdvanceClock(2000); simulator.EmitEdge(0);
        await Wait(status => status.Laps[0] == scored.Laps[0] + 1, "first resumed crossing counts");
        Check(serial.CaptureTestStatus().BestLaps[0] == scored.BestLaps[0], "first resumed crossing cannot set a fast lap");

        int laps = serial.CaptureTestStatus().Laps[0];
        simulator.AdvanceClock(100000);
        simulator.ResetController(preserveClock: true, announce: false);
        await Wait(status => status.Ready && status.Session != session && status.HeatState == HeatRaceState.Paused,
            "unannounced logical reset with higher clock pauses and re-verifies");
        Check(serial.CaptureTestStatus().Laps[0] == laps && simulator.Status.PowerMask == 0, "new-session recovery preserves laps and cuts virtual power");
        foreach (string kind in new[] { "edge", "heartbeat", "hello", "off" }) simulator.ReplayPrevious(kind);
        simulator.SendInvalidChecksum();
        await Task.Delay(250, cancellation);
        Check(serial.CaptureTestStatus().Laps[0] == laps && serial.CaptureTestStatus().Session == simulator.Status.Session &&
            serial.CaptureTestStatus().ConfirmedTimestamp < simulator.Status.Timestamp + 10000,
            "retired frames and invalid checksum cannot alter scoring, clock, or identity");

        await StartHeat();
        simulator.SetSilenced(true);
        await Wait(status => status.HeatState == HeatRaceState.Paused && !status.Ready, "open but silent transport pauses on heartbeat expiry");
        simulator.SetSilenced(false);
        await Wait(status => status.Ready, "silence recovery handshake");
        serial.HandleSpaceBar();
        await Wait(status => status.Countdown, "resume countdown starts");
        simulator.SetOnline(false);
        await Wait(status => !status.Countdown && status.HeatState == HeatRaceState.Paused, "disconnect cancels countdown");
        simulator.SetOnline(true);
        await Wait(status => status.Ready, "countdown outage reconnect");
        await Task.Delay(1700, cancellation);
        Check(serial.CaptureTestStatus().HeatState == HeatRaceState.Paused && simulator.Status.PowerMask == 0,
            "cancelled countdown cannot turn power back on later");

        serial.SetPracticeMode();
        serial.ConfigureHeatRace("TCP qualifying fixture", 60, 60, new[] { "Fixture A", "Fixture B" }, 2, LaneConfiguration.CreateDefaults(), 155);
        serial.ConfigureQualifying(0, 3);
        serial.HandleSpaceBar();
        await Wait(status => status.QualifyingState == QualifyingState.Running, "qualifying starts");
        simulator.EmitEdge(0); await Task.Delay(100, cancellation);
        simulator.AdvanceClock(1000); simulator.EmitEdge(0);
        simulator.AdvanceClock(4000);
        await Wait(status => status.QualifyingState == QualifyingState.Ready && status.Qualifier == "Fixture B", "first qualifier completes");
        serial.HandleSpaceBar();
        await Wait(status => status.QualifyingState == QualifyingState.Running, "second qualifier starts");
        simulator.ResetController(announce: false);
        await Wait(status => status.Ready && status.QualifyingState == QualifyingState.Ready, "reset interrupts qualifier for rerun");
        Check(serial.CaptureTestStatus().Qualifier == "Fixture B" && serial.CaptureTestStatus().CompletedQualifiers == 1,
            "interrupted qualifier reruns without losing prior results");
        serial.SetPracticeMode();
        serial.ConfigureHeatRace("TCP Lap Finish", 1, 60, new[] { "Fixture A", "Fixture B" }, 2,
            LaneConfiguration.CreateDefaults(), 155, format: RaceFormat.LapFinish, lapTarget: 2);
        await StartHeat();
        simulator.EmitEdge(0); simulator.EmitEdge(1); await Task.Delay(100, cancellation);
        simulator.AdvanceClock(2000); simulator.EmitEdge(0); simulator.EmitEdge(1);
        await Wait(status => status.Laps[0] == 1 && status.Laps[1] == 1, "Lap Finish continues beyond ignored duration");
        serial.HandleSpaceBar();
        await Wait(status => status.HeatState == HeatRaceState.Paused, "Lap Finish track call");
        Check(!serial.AdjustStoppedHeatLap(0, 1), "manual adjustment cannot manufacture a winning lap");
        await StartHeat();
        simulator.AdvanceClock(2000); simulator.EmitEdge(0);
        await Wait(status => status.HeatState == HeatRaceState.Complete && simulator.Status.PowerMask == 0,
            "winning crossing ends race and turns virtual track power off");
        simulator.EmitEdge(1); simulator.AdvanceClock(2000); simulator.EmitEdge(0);
        await Task.Delay(250, cancellation);
        Check(serial.CaptureTestStatus().Laps[0] == 2 && serial.CaptureTestStatus().Laps[1] == 1,
            "post-finish frames cannot alter either racer");
        Check(Directory.GetFiles(HeatRaceReportWriter.GetReportDirectory(), "LapFinish_*.json").Length == 1,
            "real TCP finish automatically exports its result");
        foreach (Form report in form.OwnedForms) report.Close();
        var lapDemo = (ToolStripMenuItem)form.GetType().GetField("_lapFinishDemoMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        lapDemo.PerformClick();
        var heat = (HeatRaceController)typeof(Serial).GetField("_heatRace", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(serial)!;
        Check(serial.DemoLapStreamActive && heat.Format == RaceFormat.LapFinish && heat.LapTarget == 10 &&
            heat.TotalHeats == 1 && heat.GetSnapshot(0).LaneRacers.Count(name => !string.IsNullOrWhiteSpace(name)) == 2 &&
            heat.GetSnapshot(0).LaneRacers.Where(name => !string.IsNullOrWhiteSpace(name)).All(name => name.StartsWith("Demo Racer ")),
            "Lap Finish demo menu configures ten laps and one racer per active lane with simulated input");
        serial.HandleSpaceBar();
        await Wait(status => status.HeatState == HeatRaceState.Running && !status.Countdown, "Lap Finish demo countdown starts racing");
        var timedDemo = (ToolStripMenuItem)form.GetType().GetField("demoRaceToolStripMenuItem", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        timedDemo.PerformClick();
        Check(heat.Format == RaceFormat.HeatRace && heat.HeatLengthSeconds == 60 && heat.BetweenHeatsSeconds == 5 &&
            heat.TotalHeats == 4 && heat.LapTarget == 0 && serial.DemoLapStreamActive,
            "existing timed demo remains unchanged when switching from Lap Finish demo");
        Check(form.Text.Contains("YATSS"), "actual application form stayed alive throughout TCP faults");
    }
}
