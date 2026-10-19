using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using YATSS;

internal sealed record ValidationResult(string Suite, string Status, int? ExitCode, double Seconds, string Log, string? Error);

internal static class RegressionRunner
{
    internal static readonly string[] Suites = { "runner", "help", "archives", "unoq-update", "sessions", "tcp", "groups", "recovery", "duration", "reruns", "exports", "distance", "lap-finish", "core" };

    internal static bool RunSuite(string name)
    {
        switch (name)
        {
            case "runner": ValidationRunnerTests.Run(); break;
            case "fixture-pass": Console.WriteLine("Fixture passed."); break;
            case "fixture-fail": Console.Error.WriteLine("Deliberate runner test failure."); Environment.ExitCode = 17; break;
            case "fixture-hang": Thread.Sleep(Timeout.Infinite); break;
            case "help": SupportTests.Run(); break;
            case "archives": ArchiveTests.Run(); break;
            case "unoq-update": UnoQUpdateTests.Run(); break;
            case "sessions": TimingSessionTests.Run(); break;
            case "tcp": TcpSimulatorTests.Run(); break;
            case "groups": GroupedHeatTests.Run(); break;
            case "recovery": RecoveryTests.Run(); break;
            case "duration": HeatDurationTests.Run(); break;
            case "reruns": RerunTests.Run(); break;
            case "exports": ExportRecoveryTests.Run(); break;
            case "distance": DistanceTests.Run(); break;
            case "lap-finish": LapFinishTests.Run(); break;
            case "core": return false;
            default: throw new ArgumentException("Unknown regression suite: " + name);
        }
        return true;
    }

    internal static ProcessStartInfo ChildStart()
    {
        ProcessStartInfo start = new(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        return start;
    }

    internal static int Run(string outputDirectory, int timeoutSeconds = 600, IReadOnlyList<string>? selectedSuites = null)
    {
        IReadOnlyList<string> suites = selectedSuites ?? Suites;
        string directory = Path.Combine(Path.GetFullPath(outputDirectory),
            "validation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        var results = new List<ValidationResult>();
        string build = BuildIdentity.GetDisplayVersion(typeof(BuildIdentity).Assembly.GetName().Version!.ToString());
        DateTimeOffset started = DateTimeOffset.Now;
        string? wine = Environment.GetEnvironmentVariable("YATSS_VALIDATION_WINE_VERSION");
        Console.WriteLine("YATSS regression validation " + build);
        Console.WriteLine("Results: " + directory);
        void SaveReport()
        {
            var report = new
            {
                schemaVersion = 1, build, started, finished = DateTimeOffset.Now, os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
                wine = wine ?? "Not supplied (Windows runner or direct invocation)",
                complete = results.Count == suites.Count,
                passed = results.Count == suites.Count && results.All(result => result.Status == "PASS"), results
            };
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            StringBuilder summary = new();
            summary.AppendLine($"YATSS Software Regression: {(report.passed ? "PASS" : report.complete ? "FAIL" : "INCOMPLETE")}");
            summary.AppendLine($"Build: {build}\nStarted: {started:O}\nOS: {report.os}\nArchitecture: {report.architecture}\nRuntime: {report.runtime}\nWine: {report.wine}\n");
            foreach (var result in results)
                summary.AppendLine($"{result.Status,-12} {result.Suite,-10} {result.Seconds:F1}s  exit={result.ExitCode}  {result.Log}" +
                    (result.Error == null ? "" : "\n  " + result.Error));
            summary.AppendLine("\nSoftware regression only; not a physical timing/safety certification or a complete UI acceptance test.");
            File.WriteAllText(Path.Combine(directory, "summary.txt"), summary.ToString());
        }
        SaveReport();
        foreach (string suite in suites)
        {
            var start = ChildStart();
            start.ArgumentList.Add("--regression-suite"); start.ArgumentList.Add(suite);
            start.Environment["YATSS_VALIDATION_ROOT"] = Path.Combine(directory, "TestData", suite);
            Stopwatch timer = Stopwatch.StartNew();
            string logName = suite + ".log";
            ValidationResult result;
            try
            {
                using Process child = Process.Start(start) ?? throw new InvalidOperationException("Could not start suite process.");
                Task<string> stdout = child.StandardOutput.ReadToEndAsync();
                Task<string> stderr = child.StandardError.ReadToEndAsync();
                bool completed = child.WaitForExit(timeoutSeconds * 1000);
                if (!completed)
                {
                    try { child.Kill(entireProcessTree: true); }
                    catch (Exception ex) when (ex is PlatformNotSupportedException or System.ComponentModel.Win32Exception or InvalidOperationException)
                    { if (!child.HasExited) child.Kill(); }
                    child.WaitForExit(10000);
                }
                // Bound pipe draining too: a stuck descendant can retain inherited handles.
                bool drained = Task.WhenAll(stdout, stderr).Wait(TimeSpan.FromSeconds(10));
                string output = stdout.IsCompletedSuccessfully ? stdout.Result : "Output pipe did not close.\n";
                string errors = stderr.IsCompletedSuccessfully ? stderr.Result : "Error pipe did not close.\n";
                File.WriteAllText(Path.Combine(directory, logName), output + "\n--- stderr ---\n" + errors);
                int? exit = child.HasExited ? child.ExitCode : null;
                string status = !completed || !drained ? "TIMEOUT" : exit == 0 ? "PASS" : "FAIL";
                result = new(suite, status, exit, timer.Elapsed.TotalSeconds, logName,
                    status == "PASS" ? null : status == "TIMEOUT" ? "Suite exceeded its execution/output timeout." :
                    "Assertion or runtime failure; see the suite log for the exception/runtime diagnostics.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                File.WriteAllText(Path.Combine(directory, logName), ex.ToString());
                result = new(suite, "LAUNCH/IO", null, timer.Elapsed.TotalSeconds, logName, ex.Message);
            }
            results.Add(result); SaveReport();
            Console.WriteLine($"{result.Status}: {suite} ({result.Seconds:F1}s)");
        }
        bool passed = results.All(result => result.Status == "PASS");
        Console.WriteLine(passed ? "All regression suites passed." : "Validation failed; inspect summary.txt and suite logs.");
        return passed ? 0 : 1;
    }
}
