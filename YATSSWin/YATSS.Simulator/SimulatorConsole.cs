using System.Text.Json;

namespace YATSS.Simulation;

public static class SimulatorConsole
{
    public static void Run(TcpControllerSimulator simulator)
    {
        Console.WriteLine("Enter help for simulator commands; quit ends the session.");
        while (Console.ReadLine() is { } line)
            if (!Execute(simulator, line, Console.Out)) return;
    }

    public static bool Execute(TcpControllerSimulator simulator, string line, TextWriter output)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            switch (parts)
            {
                case []: break;
                case ["quit"]: return false;
                case ["status"]: output.WriteLine(JsonSerializer.Serialize(simulator.Status)); break;
                case ["edge", var lane]: simulator.EmitEdge(int.Parse(lane) - 1); break;
                case ["bad-edge", var lane]: simulator.EmitEdge(int.Parse(lane) - 1, corrupt: true); break;
                case ["laps", "off"]: simulator.SetAutomaticLaps(0); break;
                case ["laps", var milliseconds]: simulator.SetAutomaticLaps(int.Parse(milliseconds)); break;
                case ["disconnect"]: simulator.Disconnect(); break;
                case ["offline"]: simulator.SetOnline(false); break;
                case ["online"]: simulator.SetOnline(true); break;
                case ["silence", "on"]: simulator.SetSilenced(true); break;
                case ["silence", "off"]: simulator.SetSilenced(false); break;
                case ["heartbeat", "off"]: simulator.SetHeartbeats(false); break;
                case ["heartbeat", "on"]: simulator.SetHeartbeats(true); break;
                case ["reset"]: simulator.ResetController(); break;
                case ["logical-reset"]: simulator.ResetController(preserveClock: true); break;
                case ["silent-reset"]: simulator.ResetController(announce: false); break;
                case ["silent-logical-reset"]: simulator.ResetController(preserveClock: true, announce: false); break;
                case ["stale", var kind]: simulator.ReplayPrevious(kind); break;
                case ["advance", var milliseconds]: simulator.AdvanceClock(uint.Parse(milliseconds)); break;
                case ["clock", var timestamp]: simulator.SetClock(uint.Parse(timestamp)); break;
                case ["bad-checksum"]: simulator.SendInvalidChecksum(); break;
                case ["help"]:
                    output.WriteLine("edge 1..8 | bad-edge 1..8 | laps <milliseconds>/off | status\n" +
                        "disconnect | offline | online | silence on/off | heartbeat on/off\n" +
                        "reset | logical-reset | silent-reset | silent-logical-reset\n" +
                        "stale edge/heartbeat/hello/off | advance <ms> | clock <uint32-ms> | bad-checksum | quit");
                    break;
                default: output.WriteLine("Unknown command. Enter help."); break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or InvalidOperationException)
        { output.WriteLine(ex.Message); }
        return true;
    }
}
