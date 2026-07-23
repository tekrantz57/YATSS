using System.Diagnostics;

namespace YATSS
{
    internal sealed class HeatRaceReportForm : Form
    {
        private readonly string _reportPath;

        public HeatRaceReportForm(string reportPath)
        {
            _reportPath = Path.GetFullPath(reportPath);
            Text = "YATSS Race Report";
            StartPosition = FormStartPosition.CenterParent;
            Width = 1100;
            Height = 800;
            MinimumSize = new Size(700, 500);

            ToolStrip toolbar = new()
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Dock = DockStyle.Top
            };
            ToolStripButton openInBrowser = new("Open in Browser")
            {
                ToolTipText = "Open this race report in the default browser"
            };
            openInBrowser.Click += (_, _) => HeatRaceReportWriter.Open(_reportPath);

            ToolStripButton openFolder = new("Open Folder")
            {
                ToolTipText = "Show this race report in File Explorer"
            };
            openFolder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_reportPath}\"")
            {
                UseShellExecute = true
            });
            toolbar.Items.Add(openInBrowser);
            toolbar.Items.Add(openFolder);

            WebBrowser reportBrowser = new()
            {
                Dock = DockStyle.Fill,
                ScriptErrorsSuppressed = true,
                Url = new Uri(_reportPath)
            };

            Controls.Add(reportBrowser);
            Controls.Add(toolbar);
        }
    }
}
