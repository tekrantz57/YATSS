namespace YATSS;

public partial class YATSS
{
    private string GetVersionInformation() => HelpSupport.VersionInformation(
        BuildIdentity.GetDisplayVersion(Application.ProductVersion), s?.CurrentControllerIdentity,
        s?.DemoLapStreamActive ?? false);

    private void ConfigureHelpMenu()
    {
        ToolStripMenuItem help = HelpSupport.CreateMenu(
            document => HelpSupport.RunAction(this, () => HelpSupport.ShowDocument(this, document)),
            () => HelpSupport.RunAction(this, () => HelpSupport.OpenWeb("https://github.com/tekrantz57/YATSS/issues/new/choose")),
            () => HelpSupport.RunAction(this, () => Clipboard.SetText(GetVersionInformation())),
            () => MessageBox.Show(this,
                GetVersionInformation() + "\n\nFree, open-source software. MIT License.\nDevelopment/beta software; installation and physical timing must be verified.",
                "About YATSS", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menuStrip1.Items.Add(help);
    }
}
