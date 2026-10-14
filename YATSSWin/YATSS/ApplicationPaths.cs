namespace YATSS;

internal static class ApplicationPaths
{
    // Only the regression executable sets this process-local value, before database access.
    internal static string? TestDataDirectory => AppContext.GetData("YATSS.TestDataDirectory") as string;
    internal static string DataDirectory => TestDataDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YATSS");
    internal static string BackupDirectory => TestDataDirectory == null ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YATSS Backups") : Path.Combine(TestDataDirectory, "Backups");
    internal static string ReportDirectory => TestDataDirectory == null ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YATSS Race Reports") : Path.Combine(TestDataDirectory, "Reports");
}
