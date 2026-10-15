using YATSS.Simulation;

int port = 45992;
if (args.Length != 0 && (args.Length != 2 || args[0] != "--port" || !int.TryParse(args[1], out port)))
{
    Console.Error.WriteLine("Usage: YATSS.Simulator [--port 45992]");
    return 2;
}
await using TcpControllerSimulator simulator = new(port, message => Console.WriteLine(message));
Console.WriteLine($"YATSS TEST CONTROLLER - virtual power only, no hardware access\nConfigure YATSS: TCP:127.0.0.1:{simulator.Port}");
Console.WriteLine("An ordinary app connection uses its normal data. Prefer YATSS.Tests --tcp-test-ui for isolated testing.");
SimulatorConsole.Run(simulator);
return 0;
