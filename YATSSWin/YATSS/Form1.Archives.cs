namespace YATSS;

public partial class YATSS
{
    private void OpenRaceArchives()
    {
        if (!s.BeginArchiveBrowsing()) return;
        try
        {
            using RaceArchiveBrowser browser = new(s.ListRaceArchives, s.ReadRaceArchive,
                s.FindOlderArchives, s.DeleteRaceArchives, ShowHeatRaceReport,
                new(ExportRaceJson, ExportRaceCsv));
            browser.ShowDialog(this);
        }
        finally { s.EndArchiveBrowsing(); }
    }
}
