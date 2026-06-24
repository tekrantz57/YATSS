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
            html.AppendLine("<html><head><meta charset=\"utf-8\"><title>Heat Race Results</title>");
            html.AppendLine("<style>");
            html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:32px;color:#202020}");
            html.AppendLine("h1{margin:0 0 6px;font-size:28px} h2{margin-top:28px;font-size:20px}");
            html.AppendLine("table{border-collapse:collapse;width:100%;margin-top:10px} th,td{border:1px solid #bbb;padding:6px 8px;text-align:right} th:first-child,td:first-child{text-align:left}");
            html.AppendLine("th{background:#efefef}.highlight{background:#fff0a8;font-weight:700}.muted{color:#666}.total{font-weight:700}");
            html.AppendLine("</style></head><body>");
            html.AppendLine("<h1>Heat Race Results</h1>");
            html.AppendLine($"<div class=\"muted\">Created {WebUtility.HtmlEncode(report.CreatedLocal.ToString("g", CultureInfo.CurrentCulture))}</div>");
            AppendFinishOrder(html, report);
            AppendFastLaps(html, report, fastestByLane);
            html.AppendLine("</body></html>");
            return html.ToString();
        }

        private static void AppendFinishOrder(StringBuilder html, HeatRaceReport report)
        {
            html.AppendLine("<h2>Finish Order</h2>");
            html.AppendLine("<table><thead><tr><th>Place</th><th>Racer</th><th>Total Laps</th>");
            for (int heat = 1; heat <= HeatRaceController.TotalHeats; heat++)
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

        private static void AppendFastLaps(StringBuilder html, HeatRaceReport report, IReadOnlyList<int?> fastestByLane)
        {
            html.AppendLine("<h2>Fast Laps By Lane</h2>");
            html.AppendLine("<table><thead><tr><th>Racer</th>");
            foreach (string laneName in report.LaneNames)
            {
                html.AppendLine($"<th>{WebUtility.HtmlEncode(laneName)}</th>");
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

        private static string FormatLap(int? milliseconds) =>
            milliseconds.HasValue
                ? (milliseconds.Value / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)
                : string.Empty;

        private static string FormatCount(int value) =>
            value > 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }
}
