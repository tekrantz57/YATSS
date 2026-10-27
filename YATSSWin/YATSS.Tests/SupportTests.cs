using System.Reflection;
using YATSS;

internal static class SupportTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Installer help: " + message); }

    public static void Run()
    {
        FirmwareAvailabilityTests.Run();
        using (Mutex marker = new(false, global::YATSS.Program.InstallerMutexName))
        using (Mutex visible = Mutex.OpenExisting(global::YATSS.Program.InstallerMutexName))
            Check(!visible.SafeWaitHandle.IsInvalid, "running-app marker is visible to setup without taking ownership");
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
            using (var sensors = new PracticeLaneSetup(LaneConfiguration.CreateDefaults(), 8, 128))
            {
                sensors.Size = sensors.MinimumSize;
                sensors.Show(); Application.DoEvents();
                Check(sensors.SelectedMask == 128, "Practice lane eight maps to capture mask 80");
                Capture(sensors, Path.Combine(TestWorkspace.Root, "practice-sensor-lanes.png"));
                var list = Field<CheckedListBox>(sensors, "_lanes");
                list.SetItemChecked(7, false);
                Check(sensors.SelectedMask == 0, "Practice allows all sensors disarmed");
                for (int lane = 0; lane < 8; lane++) list.SetItemChecked(lane, true);
                Check(sensors.SelectedMask == 255, "Practice can arm all eight sensors");
                sensors.Close();
            }
            List<string> actions = new();
            using var menu = HelpSupport.CreateMenu(document => actions.Add(document.FileName),
                () => actions.Add("bug"), () => actions.Add("copy"), () => actions.Add("about"));
            foreach (ToolStripItem item in menu.DropDownItems)
                if (item is ToolStripMenuItem command) command.PerformClick();
            Check(actions.SequenceEqual(HelpSupport.Documents.Select(document => document.FileName).Concat(new[] { "bug", "copy", "about" })),
                "menu actions dispatch to correct guides/commands");
            const string contents = "# Offline\nNo network needed.\nsystemctl --user restart yatss-unoq-updater.service";
            string? copied = null;
            bool opened = false;
            using Form viewer = HelpSupport.CreateDocumentViewer("Offline", contents, _ => throw new Exception("Unexpected network"),
                () => opened = true, value => copied = value);
            Check(((RichTextBox)viewer.Controls[0]).ReadOnly && ((RichTextBox)viewer.Controls[0]).Text.Contains("No network needed"), "offline selectable viewer");
            RichTextBox text = (RichTextBox)viewer.Controls[0];
            var toolbar = viewer.Controls.OfType<ToolStrip>().Single();
            var selection = (ToolStripButton)toolbar.Items[0];
            Check(!selection.Enabled && !text.HideSelection, "copy selection disabled until selected; selection remains visible without focus");
            text.Select(text.Text.IndexOf("systemctl", StringComparison.Ordinal), text.Text.Length - text.Text.IndexOf("systemctl", StringComparison.Ordinal));
            Check(selection.Enabled, "selection enables copy control");
            selection.PerformClick();
            Check(copied == "systemctl --user restart yatss-unoq-updater.service", "copy selected command without altering its text");
            toolbar.Items[1].PerformClick();
            Check(copied == contents, "copy all includes unselected document contents");
            toolbar.Items[3].PerformClick();
            Check(opened, "open original uses the supplied local-document action");
            text.Select(0, 0);
            Check(!selection.Enabled, "clearing selection disables copy selection");
            using Form noOriginal = HelpSupport.CreateDocumentViewer("Generated", "", _ => { });
            var emptyToolbar = noOriginal.Controls.OfType<ToolStrip>().Single();
            Check(!emptyToolbar.Items[3].Enabled, "unavailable source is not offered");
        });
        Console.WriteLine("Help menu, copy controls, original document access, offline guides, version privacy and packaging tests passed.");
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
            var scheduling = Field<ComboBox>(setup, "_scheduling");
            scheduling.SelectedIndex = 1; Application.DoEvents();
            Check(setup.Format == RaceFormat.HeatRace && setup.Scheduling == HeatRaceScheduling.IndependentGroups &&
                !Field<DataGridView>(setup, "_laneGrid").Visible, "ordinary independent group choice");
            Capture(setup, Path.Combine(directory, "independent-heat-setup.png"));
            format.SelectedIndex = 1; Application.DoEvents();
            Check(!scheduling.Enabled, "distance grouping cannot be disabled");
            format.SelectedIndex = 0; Application.DoEvents();
            Check(scheduling.Enabled && setup.Scheduling == HeatRaceScheduling.IndependentGroups, "ordinary scheduling retained across format switches");
            setup.Close();
            using (Form guide = HelpSupport.CreateDocumentViewer("UNO Q Updates", "# UNO Q Updates\n\nsystemctl --user restart yatss-unoq-updater.service", _ => { }, () => { }))
            {
                guide.Size = guide.MinimumSize;
                guide.Show(); Application.DoEvents();
                var toolbar = guide.Controls.OfType<ToolStrip>().Single();
                Check(toolbar.Items.Cast<ToolStripItem>().All(item => item.Bounds.Right <= toolbar.ClientSize.Width), "help commands fit minimum-size window");
                var text = guide.Controls.OfType<RichTextBox>().Single();
                text.SelectAll(); Application.DoEvents();
                Check(text.Top >= toolbar.Bottom && text.Bottom <= guide.ClientSize.Height, "help text does not overlap toolbar");
                Capture(guide, Path.Combine(directory, "help-copy-controls.png"));
                guide.Close();
            }
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
