using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;

namespace tlp
{
    internal static class HeatRaceReportWriter
    {
        public static string Write(HeatRaceReport report)
        {
            string reportDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Laps Race Reports");
            Directory.CreateDirectory(reportDirectory);

            string fileName = $"HeatRace_{report.CreatedLocal:yyyyMMdd_HHmmss}.html";
            string path = Path.Combine(reportDirectory, fileName);
            File.WriteAllText(path, BuildHtml(report), new UTF8Encoding(false));
            return path;
        }

        public static void Open(string path)
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }

        private static string BuildHtml(HeatRaceReport report)
        {
            int?[] fastestByLane = GetFastestByLane(report);
            StringBuilder html = new();
            html.AppendLine("<!doctype html>");
            string reportTitle = string.IsNullOrWhiteSpace(report.RaceName)
                ? "Heat Race Results"
                : $"{report.RaceName} - Heat Race Results";
            html.AppendLine($"<html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(reportTitle)}</title>");
            html.AppendLine("<style>");
            html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:32px;color:#202020}");
            html.AppendLine("h1{margin:0 0 6px;font-size:28px} h2{margin-top:28px;font-size:20px}");
            html.AppendLine("table{border-collapse:collapse;width:100%;margin-top:10px} th,td{border:1px solid #bbb;padding:6px 8px;text-align:right} th:first-child,td:first-child{text-align:left}");
            html.AppendLine("th{background:#efefef}.highlight{background:#fff0a8;font-weight:700}.heat-alt{background:#dfe8f2}.muted{color:#666}.total{font-weight:700}");
            html.AppendLine("</style></head><body>");
            html.AppendLine($"<h1>{WebUtility.HtmlEncode(reportTitle)}</h1>");
            html.AppendLine("<table style=\"width:auto;margin-top:8px\"><tbody>");
            AppendMetadataRow(html, "Created", report.CreatedLocal.ToString("g", CultureInfo.CurrentCulture));
            if (!string.IsNullOrWhiteSpace(report.RaceName))
            {
                AppendMetadataRow(html, "Race name", report.RaceName);
            }
            AppendMetadataRow(html, "Heat length", $"{report.HeatLengthMinutes} minute(s)");
            AppendMetadataRow(html, "Between heats", $"{report.BetweenHeatsSeconds} second(s)");
            AppendMetadataRow(
                html,
                "Track length",
                $"{report.TrackLengthFeet.ToString("0.00", CultureInfo.CurrentCulture)} ft");
            AppendMetadataRow(html, "Racers", report.Racers.Count.ToString(CultureInfo.InvariantCulture));
            html.AppendLine("</tbody></table>");
            if (!string.IsNullOrWhiteSpace(report.Notes))
            {
                html.AppendLine($"<p class=\"muted\">{WebUtility.HtmlEncode(report.Notes)}</p>");
            }
            AppendQualifyingResults(html, report);
            AppendFinishOrder(html, report);
            AppendFastLaps(html, report, fastestByLane);
            AppendHeatDetails(html, report);
            html.AppendLine("</body></html>");
            return html.ToString();
        }

        private static void AppendMetadataRow(StringBuilder html, string label, string value)
        {
            html.AppendLine("<tr>");
            html.AppendLine($"<th>{WebUtility.HtmlEncode(label)}</th>");
            html.AppendLine($"<td>{WebUtility.HtmlEncode(value)}</td>");
            html.AppendLine("</tr>");
        }

        private static void AppendFinishOrder(StringBuilder html, HeatRaceReport report)
        {
            html.AppendLine("<h2>Finish Order</h2>");
            html.AppendLine("<table><thead><tr><th>Place</th><th>Racer</th><th>Total Laps</th>");
            for (int heat = 1; heat <= report.TotalHeats; heat++)
            {
                html.AppendLine($"<th>Heat {heat}</th>");
            }

            html.AppendLine("</tr></thead><tbody>");
            for (int i = 0; i < report.Racers.Count; i++)
            {
                HeatRaceRacerReport racer = report.Racers[i];
                html.AppendLine("<tr>");
                html.AppendLine($"<td>{i + 1}</td>");
                html.AppendLine($"<td>{WebUtility.HtmlEncode(racer.RacerName)}</td>");
                html.AppendLine($"<td class=\"total\">{racer.TotalLaps}</td>");
                foreach (int heatLaps in racer.HeatLaps)
                {
                    html.AppendLine($"<td>{FormatCount(heatLaps)}</td>");
                }

                html.AppendLine("</tr>");
            }

            html.AppendLine("</tbody></table>");
        }

        private static void AppendQualifyingResults(StringBuilder html, HeatRaceReport report)
        {
            if (report.QualifyingResults.Count == 0)
            {
                return;
            }

            html.AppendLine("<h2>Qualifying</h2>");
            html.AppendLine("<table><thead><tr><th>Position</th><th>Racer</th><th>Best Lap</th></tr></thead><tbody>");
            for (int i = 0; i < report.QualifyingResults.Count; i++)
            {
                QualifyingResult result = report.QualifyingResults[i];
                html.AppendLine("<tr>");
                html.AppendLine($"<td>{i + 1}</td>");
                html.AppendLine($"<td>{WebUtility.HtmlEncode(result.RacerName)}</td>");
                html.AppendLine($"<td>{(result.BestLapMilliseconds.HasValue ? FormatLap(result.BestLapMilliseconds) : "No valid lap")}</td>");
                html.AppendLine("</tr>");
            }

            html.AppendLine("</tbody></table>");
        }

        private static void AppendHeatDetails(StringBuilder html, HeatRaceReport report)
        {
            html.AppendLine("<h2>Heat Details</h2>");
            html.AppendLine("<table><thead><tr><th>Heat</th><th>Lane</th><th>Racer</th><th>Heat Laps</th><th>Total Laps</th><th>Best Lap</th></tr></thead><tbody>");
            foreach (HeatRaceLaneResult result in report.LaneResults)
            {
                string rowClass = result.HeatNumber % 2 == 0 ? " class=\"heat-alt\"" : string.Empty;
                html.AppendLine($"<tr{rowClass}>");
                html.AppendLine($"<td>{result.HeatNumber}</td>");
                html.AppendLine($"<td style=\"{GetLaneCellStyle(report, result.LaneIndex)}\">{WebUtility.HtmlEncode(result.LaneName)}</td>");
                html.AppendLine($"<td>{WebUtility.HtmlEncode(result.RacerName)}</td>");
                html.AppendLine($"<td>{FormatCount(result.HeatLaps)}</td>");
                html.AppendLine($"<td>{FormatCount(result.TotalLaps)}</td>");
                html.AppendLine($"<td>{FormatLap(result.BestLapMilliseconds)}</td>");
                html.AppendLine("</tr>");
            }

            html.AppendLine("</tbody></table>");
        }

        private static void AppendFastLaps(StringBuilder html, HeatRaceReport report, IReadOnlyList<int?> fastestByLane)
        {
            html.AppendLine("<h2>Fast Laps By Lane</h2>");
            html.AppendLine("<table><thead><tr><th>Racer</th>");
            for (int lane = 0; lane < report.LaneNames.Count; lane++)
            {
                html.AppendLine($"<th style=\"{GetLaneCellStyle(report, lane)}\">{WebUtility.HtmlEncode(report.LaneNames[lane])}</th>");
            }

            html.AppendLine("</tr></thead><tbody>");
            foreach (HeatRaceRacerReport racer in report.Racers.OrderBy(racer => racer.RacerName, StringComparer.OrdinalIgnoreCase))
            {
                html.AppendLine("<tr>");
                html.AppendLine($"<td>{WebUtility.HtmlEncode(racer.RacerName)}</td>");
                for (int lane = 0; lane < report.LaneNames.Count; lane++)
                {
                    int? lap = lane < racer.BestLapByLaneMilliseconds.Count ? racer.BestLapByLaneMilliseconds[lane] : null;
                    bool highlight = lap.HasValue && lane < fastestByLane.Count && fastestByLane[lane] == lap;
                    string cssClass = highlight ? " class=\"highlight\"" : string.Empty;
                    html.AppendLine($"<td{cssClass}>{FormatLap(lap)}</td>");
                }

                html.AppendLine("</tr>");
            }

            html.AppendLine("</tbody></table>");
        }

        private static int?[] GetFastestByLane(HeatRaceReport report)
        {
            int?[] fastest = new int?[report.LaneNames.Count];
            foreach (HeatRaceRacerReport racer in report.Racers)
            {
                for (int lane = 0; lane < racer.BestLapByLaneMilliseconds.Count && lane < fastest.Length; lane++)
                {
                    if (racer.BestLapByLaneMilliseconds[lane] is not int lap)
                    {
                        continue;
                    }

                    fastest[lane] = !fastest[lane].HasValue
                        ? lap
                        : Math.Min(fastest[lane]!.Value, lap);
                }
            }

            return fastest;
        }

        private static string GetLaneCellStyle(HeatRaceReport report, int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= report.LaneColorArgb.Count)
            {
                return string.Empty;
            }

            Color color = Color.FromArgb(report.LaneColorArgb[laneIndex]);
            double luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
            string foreground = luminance >= 150 ? "#000000" : "#ffffff";
            return $"background:#{color.R:X2}{color.G:X2}{color.B:X2};color:{foreground};font-weight:700";
        }

        private static string FormatLap(int? milliseconds) =>
            milliseconds.HasValue
                ? (milliseconds.Value / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)
                : string.Empty;

        private static string FormatCount(int value) =>
            value > 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }
}
