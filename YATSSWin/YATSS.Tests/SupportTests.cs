using System.Reflection;
using YATSS;

internal static class SupportTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Installer help: " + message); }

    public static void Run()
    {
        string version = HelpSupport.VersionInformation("git-test-dirty", new(4, 8, "ESP32_C6_DEVKITC1", "test"), true);
        Check(version.Contains("git-test-dirty") && version.Contains("ESP32_C6_DEVKITC1") &&
            version.Contains("Simulated lap input: Yes") && version.Contains("Runtime:"), "version summary includes diagnostic identity");
        Check(!version.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) &&
            !version.Contains("DatabasePath") && !version.Contains("COM5"), "summary excludes private paths and addresses");
        Check(HelpSupport.VersionInformation("test", null, false).Contains("Not identified"), "unidentified controller supported");
        foreach (HelpDocument document in HelpSupport.Documents)
            Check(File.Exists(HelpSupport.DocumentPath(AppContext.BaseDirectory, document)), "packaged guide: " + document.FileName);
        OnSta(() =>
        {
            List<string> actions = new();
            using var menu = HelpSupport.CreateMenu(document => actions.Add(document.FileName),
                () => actions.Add("bug"), () => actions.Add("copy"), () => actions.Add("about"));
            foreach (ToolStripItem item in menu.DropDownItems)
                if (item is ToolStripMenuItem command) command.PerformClick();
            Check(actions.SequenceEqual(HelpSupport.Documents.Select(document => document.FileName).Concat(new[] { "bug", "copy", "about" })),
                "menu actions dispatch to correct guides/commands");
            using Form viewer = HelpSupport.CreateDocumentViewer("Offline", "# Offline\nNo network needed.", _ => throw new Exception("Unexpected network"));
            Check(((RichTextBox)viewer.Controls[0]).ReadOnly && ((RichTextBox)viewer.Controls[0]).Text.Contains("No network needed"), "offline selectable viewer");
        });
        Console.WriteLine("Help menu, offline guides, version privacy and document packaging tests passed.");
    }

    private static T Field<T>(object form, string name) => (T)form.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }

    public static void CheckUi(string directory)
    {
        Directory.CreateDirectory(directory);
        OnSta(() =>
        {
            Application.EnableVisualStyles();
            using HeatRaceSetup setup = new(8, LaneConfiguration.CreateDefaults(), new[] { "Alice", "Bob", "Carol" }, new(180, 60, "Installer test"));
            setup.Size = setup.MinimumSize;
            setup.Show(); Application.DoEvents();
            var racers = Field<CheckedListBox>(setup, "_racerList");
            for (int index = 0; index < racers.Items.Count; index++) racers.SetItemChecked(index, true);
            Application.DoEvents();
            var format = Field<ComboBox>(setup, "_format");
            Check(format.Parent!.Controls.OfType<Label>().Any(label => label.Text == "Race format"), "format has visible label");
            Check(format.Right <= format.Parent.ClientSize.Width && format.Width > 180, "format fits minimum-size dialog");
            var add = Descendants(setup).OfType<Button>().Single(button => button.Text == "Add User...");
            Check(add.Bottom <= add.Parent!.ClientSize.Height, "Add User remains visible at minimum width");
            Capture(setup, Path.Combine(directory, "heat-setup.png"));
            format.SelectedIndex = 1; Application.DoEvents();
            Check(!Field<DataGridView>(setup, "_laneGrid").Visible && Field<GroupBox>(setup, "_selectedGroup").Text == "Qualifying Order", "distance hides provisional lanes");
            Check(Field<ListBox>(setup, "_selectedRacers").Items[0]!.ToString() == "1. Alice", "distance shows qualifying order");
            Capture(setup, Path.Combine(directory, "distance-setup.png"));
            format.SelectedIndex = 0; Application.DoEvents();
            Check(Field<DataGridView>(setup, "_laneGrid").Visible && Field<GroupBox>(setup, "_selectedGroup").Text == "Rotation Queue", "ordinary queue restored when switching back");
            setup.Close();
            using Form owner = new() { Size = new Size(600, 450) };
            MenuStrip strip = new();
            var menu = HelpSupport.CreateMenu(_ => { }, () => { }, () => { }, () => { });
            strip.Items.Add(menu); owner.Controls.Add(strip); owner.Show(); menu.ShowDropDown(); Application.DoEvents();
            using Bitmap bitmap = new(menu.DropDown.Width, menu.DropDown.Height);
            menu.DropDown.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, "help-menu.png"));
            owner.Close();
        });
    }

    private static void Capture(Form form, string path)
    {
        using Bitmap bitmap = new(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path);
    }

    private static IEnumerable<Control> Descendants(Control control) =>
        control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
