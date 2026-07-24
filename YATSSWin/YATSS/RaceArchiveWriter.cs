using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace YATSS
{
    public sealed record RaceArchive(
        int SchemaVersion,
        string ApplicationVersion,
        DateTimeOffset ExportedAt,
        HeatRaceReport Race);

    public sealed record RaceExportOptions(
        bool ExportJson = true,
        bool ExportCsv = true);

    public sealed record RaceExportPaths(
        string Html,
        string? Json,
        string? ResultsCsv,
        string? LapsCsv,
        string? QualifyingCsv,
        string? AdjustmentsCsv);

    public static class RaceArchiveWriter
    {
        public const int CurrentSchemaVersion = 1;

        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public static RaceExportPaths Write(
            HeatRaceReport report,
            string? outputDirectory = null,
            RaceExportOptions? exportOptions = null)
        {
            RaceExportOptions options = exportOptions ?? new RaceExportOptions();
            string directory = string.IsNullOrWhiteSpace(outputDirectory)
                ? HeatRaceReportWriter.GetReportDirectory()
                : Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);

            string baseName = $"HeatRace_{report.CreatedLocal:yyyyMMdd_HHmmss}";
            RaceExportPaths paths = new(
                Path.Combine(directory, $"{baseName}.html"),
                options.ExportJson ? Path.Combine(directory, $"{baseName}.json") : null,
                options.ExportCsv ? Path.Combine(directory, $"{baseName}_results.csv") : null,
                options.ExportCsv ? Path.Combine(directory, $"{baseName}_laps.csv") : null,
                options.ExportCsv ? Path.Combine(directory, $"{baseName}_qualifying.csv") : null,
                options.ExportCsv ? Path.Combine(directory, $"{baseName}_adjustments.csv") : null);

            HeatRaceReportWriter.Write(report, paths.Html);
            if (paths.Json != null)
            {
                WriteJson(report, paths.Json);
            }

            if (paths.ResultsCsv != null && paths.LapsCsv != null &&
                paths.QualifyingCsv != null && paths.AdjustmentsCsv != null)
            {
                File.WriteAllText(paths.ResultsCsv, BuildResultsCsv(report), Utf8NoBom);
                File.WriteAllText(paths.LapsCsv, BuildLapsCsv(report), Utf8NoBom);
                File.WriteAllText(paths.QualifyingCsv, BuildQualifyingCsv(report), Utf8NoBom);
                File.WriteAllText(paths.AdjustmentsCsv, BuildAdjustmentsCsv(report), Utf8NoBom);
            }
            return paths;
        }

        private static void WriteJson(HeatRaceReport report, string path)
        {
            string applicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            RaceArchive archive = new(
                CurrentSchemaVersion,
                applicationVersion,
                DateTimeOffset.Now,
                report);
            JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };
            File.WriteAllText(path, JsonSerializer.Serialize(archive, options), Utf8NoBom);
        }

        private static string BuildResultsCsv(HeatRaceReport report)
        {
            StringBuilder csv = new();
            AppendRow(csv,
                "RaceName", "CreatedLocal", "FinalPlace", "Heat", "LaneNumber", "LaneName",
                "Racer", "HeatLaps", "TotalLaps", "BestLapMilliseconds");
            Dictionary<string, int> places = report.Racers
                .Select((racer, index) => new { racer.RacerName, Place = index + 1 })
                .ToDictionary(item => item.RacerName, item => item.Place, StringComparer.OrdinalIgnoreCase);
            foreach (HeatRaceLaneResult result in report.LaneResults)
            {
                AppendRow(csv,
                    report.RaceName,
                    report.CreatedLocal.ToString("O", CultureInfo.InvariantCulture),
                    places.GetValueOrDefault(result.RacerName),
                    result.HeatNumber,
                    result.LaneIndex + 1,
                    result.LaneName,
                    result.RacerName,
                    result.HeatLaps,
                    result.TotalLaps,
                    result.BestLapMilliseconds);
            }

            return csv.ToString();
        }

        private static string BuildLapsCsv(HeatRaceReport report)
        {
            StringBuilder csv = new();
            AppendRow(csv,
                "RaceName", "Heat", "LaneNumber", "LaneName", "Racer", "LapNumberInHeat",
                "RacerTotalLapNumber", "LapMilliseconds", "RaceElapsedMilliseconds", "FastestLapEligible");
            foreach (HeatRaceLapRecord lap in report.Laps)
            {
                AppendRow(csv,
                    report.RaceName,
                    lap.HeatNumber,
                    lap.LaneIndex + 1,
                    lap.LaneName,
                    lap.RacerName,
                    lap.LapNumberInHeat,
                    lap.RacerTotalLapNumber,
                    lap.LapMilliseconds,
                    lap.RaceElapsedMilliseconds,
                    lap.FastestLapEligible);
            }

            return csv.ToString();
        }

        private static string BuildQualifyingCsv(HeatRaceReport report)
        {
            StringBuilder csv = new();
            AppendRow(csv,
                "RaceName", "Position", "OriginalOrder", "LaneNumber", "LaneName", "Racer",
                "ConfiguredDurationSeconds", "ElapsedMilliseconds", "LapNumber", "LapMilliseconds",
                "SessionElapsedMilliseconds", "IsBestLap");
            for (int position = 0; position < report.QualifyingResults.Count; position++)
            {
                QualifyingResult result = report.QualifyingResults[position];
                string laneName = result.LaneIndex >= 0 && result.LaneIndex < report.LaneNames.Count
                    ? report.LaneNames[result.LaneIndex]
                    : string.Empty;
                if (result.Laps.Count == 0)
                {
                    AppendRow(csv,
                        report.RaceName,
                        position + 1,
                        result.OriginalOrder + 1,
                        result.LaneIndex >= 0 ? result.LaneIndex + 1 : null,
                        laneName,
                        result.RacerName,
                        result.ConfiguredDurationSeconds,
                        result.ElapsedMilliseconds,
                        null,
                        null,
                        null,
                        false);
                    continue;
                }

                foreach (QualifyingLapRecord lap in result.Laps)
                {
                    AppendRow(csv,
                        report.RaceName,
                        position + 1,
                        result.OriginalOrder + 1,
                        result.LaneIndex + 1,
                        laneName,
                        result.RacerName,
                        result.ConfiguredDurationSeconds,
                        result.ElapsedMilliseconds,
                        lap.LapNumber,
                        lap.LapMilliseconds,
                        lap.SessionElapsedMilliseconds,
                        result.BestLapMilliseconds == lap.LapMilliseconds);
                }
            }

            return csv.ToString();
        }

        private static string BuildAdjustmentsCsv(HeatRaceReport report)
        {
            StringBuilder csv = new();
            AppendRow(csv,
                "RaceName", "Heat", "LaneNumber", "LaneName", "Racer", "Delta",
                "ResultingTotalLaps", "RaceElapsedMilliseconds", "RecordedAt");
            foreach (HeatRaceManualAdjustment adjustment in report.ManualAdjustments)
            {
                AppendRow(csv,
                    report.RaceName,
                    adjustment.HeatNumber,
                    adjustment.LaneIndex + 1,
                    adjustment.LaneName,
                    adjustment.RacerName,
                    adjustment.Delta,
                    adjustment.ResultingTotalLaps,
                    adjustment.RaceElapsedMilliseconds,
                    adjustment.RecordedAt.ToString("O", CultureInfo.InvariantCulture));
            }

            return csv.ToString();
        }

        private static void AppendRow(StringBuilder csv, params object?[] values)
        {
            csv.AppendLine(string.Join(",", values.Select(FormatCsvValue)));
        }

        private static string FormatCsvValue(object? value)
        {
            string text = value switch
            {
                null => string.Empty,
                bool boolean => boolean ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => value.ToString() ?? string.Empty
            };
            return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
                ? $"\"{text.Replace("\"", "\"\"")}\""
                : text;
        }
    }
}
