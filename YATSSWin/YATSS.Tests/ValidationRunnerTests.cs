using System.Text.Json;

internal static class ValidationRunnerTests
{
    internal static void Run()
    {
        string directory = Path.Combine(TestWorkspace.Root, "RunnerChecks");
        int failed = RegressionRunner.Run(directory, 60, new[] { "fixture-fail", "fixture-pass" });
        string first = Directory.GetDirectories(directory).Single();
        using (JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(first, "results.json"))))
        {
            if (failed != 1 || report.RootElement.GetProperty("passed").GetBoolean() ||
                report.RootElement.GetProperty("results")[0].GetProperty("exitCode").GetInt32() != 17 ||
                report.RootElement.GetProperty("results")[1].GetProperty("status").GetString() != "PASS")
                throw new InvalidOperationException("Runner must retain failure and continue to later suites.");
        }
        if (!File.ReadAllText(Path.Combine(first, "fixture-fail.log")).Contains("Deliberate runner test failure"))
            throw new InvalidOperationException("Runner must retain stderr.");
        int timeout = RegressionRunner.Run(directory, 1, new[] { "fixture-hang" });
        string second = Directory.GetDirectories(directory).Single(path => path != first);
        using (JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(second, "results.json"))))
            if (timeout != 1 || report.RootElement.GetProperty("results")[0].GetProperty("status").GetString() != "TIMEOUT")
                throw new InvalidOperationException("Runner must not report timed-out suites as passed.");
        Console.WriteLine("Runner failure, continuation, stderr and timeout reporting passed (nested failures above are deliberate).");
    }
}
