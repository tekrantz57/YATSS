namespace YATSS
{
    internal sealed class RaceReportService
    {
        private readonly YATSS _form;
        private readonly SerialLog _log;

        public RaceReportService(YATSS form, SerialLog log)
        {
            _form = form;
            _log = log;
        }

        public void AnnouncePodium(HeatRaceReport report)
        {
            string[] placeNames = { "First", "Second", "Third" };
            string announcement = string.Join(
                ". ",
                report.Racers
                    .Take(placeNames.Length)
                    .Select((racer, index) => $"{placeNames[index]} place, {racer.RacerName}"));
            if (!string.IsNullOrWhiteSpace(announcement))
            {
                SpeechAnnouncer.SpeakAsync(announcement, _form.SpeechVoiceName);
            }
        }

        public RaceExportPlan CreatePlan(Guid eventId) => new(eventId, DateTimeOffset.Now,
            Path.GetFullPath(HeatRaceReportWriter.GetReportDirectory()),
            new RaceExportOptions(_form.ExportRaceJson, _form.ExportRaceCsv));

        public RaceExportPaths? Write(HeatRaceReport report, RaceExportPlan plan)
        {
            try
            {
                RaceExportPaths paths = RaceArchiveWriter.Write(
                    report,
                    exportPlan: plan);
                string artifactDescription = paths switch
                {
                    { Json: not null, ResultsCsv: not null } => "HTML report, JSON archive, and CSV files",
                    { Json: not null } => "HTML report and JSON archive",
                    { ResultsCsv: not null } => "HTML report and CSV files",
                    _ => "HTML report"
                };
                string? reportDirectory = Path.GetDirectoryName(paths.Html);
                _log.Info($"heat race {artifactDescription} written to {reportDirectory}");
                _form.SetStatusMessage($"Race {artifactDescription} written: {reportDirectory}");
                return paths;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _log.Error(exception, "heat race artifact export failed");
                _form.SetStatusMessage("Race exports incomplete. Fix storage, then restart and choose Finish Exports; existing filenames will be reused.");
                return null;
            }
        }
    }
}
