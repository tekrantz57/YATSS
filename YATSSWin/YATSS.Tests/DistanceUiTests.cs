using System.Drawing;
using System.Windows.Forms;
using YATSS;

internal static class DistanceUiTests
{
    public static void Run()
    {
        Thread thread = new(() =>
        {
            Application.EnableVisualStyles();
            using Form host = new() { Text = "YATSS Distance UI Checks (No Controller)", Size = new Size(550, 180) };
            FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, Padding = new Padding(16) };
            QualifyingResult[] racers = Enumerable.Range(0, 10).Select(id => new QualifyingResult($"Test Racer {id + 1}", id, 5000)
            { Distance = DistanceScoring.Approve(10, 70, 70, "UI test") }).ToArray();
            DistanceStanding[] rows = racers.Select(result => new DistanceStanding(result.OriginalOrder,
                result.RacerName, result.OriginalOrder / 8 + 1, "Red", 1070, 12, null, 2270, 10000, 5000, null)).ToArray();
            AddButton("Approval", () => { using DistanceApprovalForm form = new("Test Racer 1", 10, 70, "Qualifying"); form.ShowDialog(host); });
            AddButton("Lane Choices", () => { using QualifyingLaneSelection form = new(racers, 8, LaneConfiguration.CreateDefaults(), true); form.ShowDialog(host); });
            AddButton("Exact Ties", () => { using DistanceTieForm form = new(rows, "Qualifying"); form.ShowDialog(host); });
            AddButton("Standings", () => { LiveStandingsForm form = new(); form.UpdateStandings(rows); form.Show(host); });
            host.Controls.Add(buttons);
            Application.Run(host);
            void AddButton(string text, Action click)
            {
                Button button = new() { Text = text, AutoSize = true };
                button.Click += (_, _) => click();
                buttons.Controls.Add(button);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
