using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using YATSS;

internal static class ExportRecoveryTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Export recovery: " + message); }

    private static HeatRaceController CompleteRace()
    {
        HeatRaceController heat = new(); heat.Configure(90, 0, new[] { "A", "B" }, 2);
        heat.Start(1000); heat.Complete(); heat.RecordHeatResults(new LapRace().GetLaneSnapshots());
        heat.PrepareNextHeat(new int[8]); heat.Start(100000); heat.Complete();
        heat.RecordHeatResults(new LapRace().GetLaneSnapshots());
        return heat;
    }

    private static RaceExportPaths Write(HeatRaceReport report, RaceExportPlan plan, Action<int> published)
    {
        MethodInfo method = typeof(RaceArchiveWriter).GetMethod("WriteCore", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { return (RaceExportPaths)method.Invoke(null, new object?[] { report, null, null, plan, published })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    public static void CrashFixture(string planPath, int stopAfter)
    {
        RaceExportPlan plan = JsonSerializer.Deserialize<RaceExportPlan>(File.ReadAllText(planPath))!;
        Write(CompleteRace().CreateReport(), plan, count =>
        {
            if (count != stopAfter) return;
            Console.WriteLine("EXPORT_FIXTURE_READY"); Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        });
    }

    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "yatss-export-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            HeatRaceController heat = CompleteRace(); HeatRaceReport report = heat.CreateReport();
            RaceExportPlan plan = new(Guid.NewGuid(), DateTimeOffset.Now, Path.Combine(directory, "reports"), new());
            string journal = Path.Combine(directory, "ActiveRace.db");
            RaceRecoveryFrame frame = new(RaceRecoveryStore.SchemaVersion, plan.EventId, DateTimeOffset.Now,
                "Export pending", heat.CaptureRecovery(0), new QualifyingController().CaptureRecovery(),
                LapRaceOptions.Default, new LapRace().CaptureRecovery(), false, false, new[] { "A", "B" }, 1800)
                { ExportPlan = plan };
            using (RaceRecoveryStore store = new(journal)) store.Save(frame);
            using (RaceRecoveryStore store = new(journal))
            {
                frame = store.Read()!;
                Check(frame.ExportPlan == plan && !frame.ReportWritten, "plan and pending flag survive restart");
            }
            RaceExportPaths first = RaceArchiveWriter.Write(report, exportPlan: frame.ExportPlan);
            string receipt = Directory.GetFiles(plan.OutputDirectory, "*.yatss-export").Single();
            ValidateReceipt(receipt);
            RaceExportPaths retry = RaceArchiveWriter.Write(report with { CreatedLocal = report.CreatedLocal.AddDays(1) },
                outputDirectory: Path.Combine(directory, "wrong"), exportOptions: new(false, false), exportPlan: plan);
            Check(first == retry && !Directory.Exists(Path.Combine(directory, "wrong")), "saved paths and options win on retry");
            Check(Directory.GetFiles(plan.OutputDirectory).Length == 7, "retry does not duplicate the six artifacts and receipt");
            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(retry.Json!)))
                Check(json.RootElement.GetProperty("race").GetProperty("createdLocal").GetDateTime() == plan.CreatedAt.DateTime,
                    "report creation time stays stable");

            // A failed replacement leaves complete old files, never truncated destinations.
            byte[] originalJson = File.ReadAllBytes(first.Json!);
            using (FileStream locked = new(first.Json!, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool refused = false;
                try { RaceArchiveWriter.Write(report, exportPlan: plan); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { refused = true; }
                Check(refused && !File.Exists(receipt), "locked replacement cannot leave a completion receipt");
            }
            Check(File.ReadAllBytes(first.Json!).SequenceEqual(originalJson), "failed replacement retains original complete file");
            Check(Directory.GetFiles(plan.OutputDirectory, "*.yatss-tmp").Length == 0, "ordinary failure cleans staged files");
            RaceArchiveWriter.Write(report, exportPlan: plan); ValidateReceipt(receipt);

            for (int boundary = 0; boundary <= 7; boundary++)
            {
                RaceExportPlan crashPlan = plan with { EventId = Guid.NewGuid(), OutputDirectory = Path.Combine(directory, "crash-" + boundary) };
                TestCrash(report, crashPlan, boundary, directory);
            }
            RaceExportPlan other = plan with { EventId = Guid.NewGuid() };
            Check(RaceArchiveWriter.Write(report, exportPlan: other).Html != first.Html,
                "different events finishing in the same second do not collide");
            RaceExportPlan html = plan with { EventId = Guid.NewGuid(), OutputDirectory = Path.Combine(directory, "html"), Options = new(false, false) };
            RaceArchiveWriter.Write(report, exportPlan: html);
            Check(Directory.GetFiles(html.OutputDirectory).Length == 2, "HTML-only event has only report and receipt");
            using (RaceRecoveryStore store = new(journal))
            {
                store.Save(frame with { ReportWritten = true });
                Check(store.Read()!.ReportWritten && store.Read()!.ExportPlan == plan, "completion flag retains saved identity");
            }
            Console.WriteLine("Restart-safe race export and forced-termination tests passed.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void TestCrash(HeatRaceReport report, RaceExportPlan plan, int boundary, string directory)
    {
        string planPath = Path.Combine(directory, "plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
        ProcessStartInfo start = new(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--export-crash-fixture"); start.ArgumentList.Add(planPath); start.ArgumentList.Add(boundary.ToString());
        using Process child = Process.Start(start)!;
        try
        {
            Task<string?> ready = child.StandardOutput.ReadLineAsync();
            Check(ready.Wait(TestWorkspace.FixtureTimeout) && ready.Result == "EXPORT_FIXTURE_READY", "child reaches export boundary " + boundary);
        }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); child.WaitForExit(); }
        foreach (string file in Directory.GetFiles(plan.OutputDirectory, "*.html"))
            Check(File.ReadAllText(file).Contains("</html>"), "published HTML is complete after termination");
        RaceExportPaths paths = RaceArchiveWriter.Write(report, exportPlan: plan);
        Check(Directory.GetFiles(plan.OutputDirectory, "*.html").Length == 1 && File.Exists(paths.Json), "retry completes one stable bundle");
        Check(Directory.GetFiles(plan.OutputDirectory, "*.yatss-tmp").Length == 0, "retry cleans crash remnants");
        ValidateReceipt(Directory.GetFiles(plan.OutputDirectory, "*.yatss-export").Single());
    }

    private static void ValidateReceipt(string receipt)
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(receipt));
        foreach (JsonElement file in json.RootElement.GetProperty("files").EnumerateArray())
        {
            string path = Path.Combine(Path.GetDirectoryName(receipt)!, file.GetProperty("name").GetString()!);
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == file.GetProperty("sha256").GetString(),
                "completion receipt hashes match every file");
        }
    }
}
