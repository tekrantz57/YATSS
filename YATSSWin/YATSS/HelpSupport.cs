using System.Diagnostics;
using System.Runtime.InteropServices;

namespace YATSS;

internal sealed record HelpDocument(string Title, string FileName, string RepositoryPath);

internal static class HelpSupport
{
    internal static readonly HelpDocument[] Documents =
    {
        new("Quick Start and Demo", "QUICK_START.md", "docs/QUICK_START.md"),
        new("Race Formats and Operation", "WINDOWS_APP.md", "YATSSWin/README.md"),
        new("Combined Distance Racing", "COMBINED_DISTANCE.md", "docs/COMBINED_DISTANCE.md"),
        new("Independent Heat Groups", "INDEPENDENT_HEAT_GROUPS.md", "docs/INDEPENDENT_HEAT_GROUPS.md"),
        new("Controller Boards and Pin Maps", "CONTROLLER_GUIDE.md", "YATSSMC/README.md"),
        new("Track-Power Wiring", "RELAY_WIRING.md", "docs/RELAY_WIRING.md"),
        new("Installer Commissioning Checklist", "COMMISSIONING_CHECKLIST.md", "docs/COMMISSIONING_CHECKLIST.md"),
        new("Troubleshooting", "TROUBLESHOOTING.md", "docs/TROUBLESHOOTING.md"),
        new("Active Event Recovery", "ACTIVE_RACE_RECOVERY.md", "docs/ACTIVE_RACE_RECOVERY.md"),
        new("Current-Heat Reruns", "HEAT_RERUNS.md", "docs/HEAT_RERUNS.md"),
        new("Reports and Data Exports", "RACE_DATA_EXPORT.md", "docs/RACE_DATA_EXPORT.md"),
        new("Race Archives and Retention", "RACE_ARCHIVES.md", "docs/RACE_ARCHIVES.md"),
        new("UNO Q Updates", "UNO_Q_UPDATES.md", "docs/UNO_Q_UPDATES.md")
    };

    internal static string DocumentPath(string directory, HelpDocument document) =>
        Path.Combine(directory, "Documentation", document.FileName);

    internal static ToolStripMenuItem CreateMenu(Action<HelpDocument> open, Action report, Action copy, Action about)
    {
        ToolStripMenuItem menu = new("Help");
        foreach (HelpDocument document in Documents)
        {
            ToolStripMenuItem item = new(document.Title);
            item.Click += (_, _) => open(document);
            menu.DropDownItems.Add(item);
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var command in new[] { ("Report a Bug...", report), ("Copy Version Information", copy), ("About YATSS", about) })
        {
            ToolStripMenuItem item = new(command.Item1);
            item.Click += (_, _) => command.Item2();
            menu.DropDownItems.Add(item);
        }
        return menu;
    }

    internal static string VersionInformation(string version, ControllerIdentity? controller, bool demo) =>
        $"YATSS {version}\nOS: {RuntimeInformation.OSDescription}\n" +
        $"Architecture: OS {RuntimeInformation.OSArchitecture}, app {RuntimeInformation.ProcessArchitecture}\n" +
        $"Runtime: {RuntimeInformation.FrameworkDescription}\nSimulated lap input: {(demo ? "Yes" : "No")}\n" +
        "Controller identity (not a live readiness check): " + (controller == null ? "Not identified" :
            $"{controller.BoardProfile}, firmware {controller.FirmwareVersion}, protocol {controller.ProtocolVersion}, {controller.LaneCount} lanes");

    internal static void OpenWeb(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    internal static void ShowDocument(Form owner, HelpDocument document)
    {
        string path = DocumentPath(AppContext.BaseDirectory, document);
        if (!File.Exists(path))
        {
            if (MessageBox.Show(owner, "This guide is missing from this application folder. Open its online copy?",
                document.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                OpenWeb("https://github.com/tekrantz57/YATSS/blob/master/" + document.RepositoryPath);
            return;
        }
        using Form viewer = CreateDocumentViewer(document.Title, File.ReadAllText(path),
            url => RunAction(owner, () => OpenWeb(url)));
        viewer.ShowDialog(owner);
    }

    internal static Form CreateDocumentViewer(string title, string contents, Action<string> openLink)
    {
        Form viewer = new()
        {
            Text = title, StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(600, 400), Size = new Size(900, 700)
        };
        RichTextBox text = new()
        {
            Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = true,
            BackColor = SystemColors.Window, Font = new Font("Segoe UI", 10),
            Text = contents, BorderStyle = BorderStyle.None
        };
        text.LinkClicked += (_, e) =>
        {
            if (Uri.TryCreate(e.LinkText, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
                openLink(uri.AbsoluteUri);
        };
        viewer.Controls.Add(text);
        return viewer;
    }

    internal static void RunAction(Form owner, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception or ExternalException or ArgumentException)
        { MessageBox.Show(owner, ex.Message, "Help Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
