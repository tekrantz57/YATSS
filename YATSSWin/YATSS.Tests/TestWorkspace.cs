using YATSS;

internal static class TestWorkspace
{
    internal static string Root { get; private set; } = "";
    internal static readonly TimeSpan FixtureTimeout = TimeSpan.FromSeconds(60);

    internal static void Initialize()
    {
        string? inherited = Environment.GetEnvironmentVariable("YATSS_VALIDATION_ROOT");
        Root = inherited == null ? Path.Combine(Path.GetTempPath(), "YATSS.Validation", Guid.NewGuid().ToString("N")) : Path.GetFullPath(inherited);
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("YATSS_VALIDATION_ROOT", Root);
        string data = Path.Combine(Root, "AppData");
        Directory.CreateDirectory(data);
        AppContext.SetData("YATSS.TestDataDirectory", data);
        // SQLite backup sidecars need room below native Windows path limits.
        string temporary = Path.Combine(Path.GetTempPath(), "YATSS.Tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temporary);
        Environment.SetEnvironmentVariable("TEMP", temporary);
        Environment.SetEnvironmentVariable("TMP", temporary);
        if (!AppDatabase.DatabasePath.Equals(Path.Combine(data, "YATSS.db"), StringComparison.OrdinalIgnoreCase) ||
            !AppDatabase.GetDefaultBackupDirectory().StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !HeatRaceReportWriter.GetReportDirectory().StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !SerialLog.CurrentPath.StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Regression data isolation could not be established; refusing to test.");
    }
}
