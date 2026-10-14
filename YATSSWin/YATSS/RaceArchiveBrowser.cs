using System.Text;
using Microsoft.Data.Sqlite;

namespace YATSS;

internal sealed class RaceArchiveBrowser : Form
{
    private readonly Func<string, int, RaceArchivePage> _list;
    private readonly Func<RaceArchiveEntry, RaceRecoveryFrame> _read;
    private readonly Func<DateTimeOffset, IReadOnlyList<RaceArchiveEntry>> _older;
    private readonly Func<IReadOnlyList<RaceArchiveEntry>, int> _delete;
    private readonly Action<string> _open;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly TextBox _search = new() { Width = 200 };
    private readonly Label _pageLabel = new() { AutoSize = true, Margin = new Padding(8) };
    private readonly CheckBox _json = new() { Text = "JSON", AutoSize = true };
    private readonly CheckBox _csv = new() { Text = "CSV", AutoSize = true };
    private readonly DateTimePicker _cutoff = new() { Format = DateTimePickerFormat.Short, Width = 110, Value = DateTime.Today.AddMonths(-6) };
    private readonly Button _previous = new() { Text = "Previous", AutoSize = true };
    private readonly Button _next = new() { Text = "Next", AutoSize = true };
    private readonly Button _details = new() { Text = "Details", AutoSize = true };
    private readonly Button _original = new() { Text = "Open Original Report", AutoSize = true };
    private readonly Button _export = new() { Text = "Export Copy...", AutoSize = true };
    private readonly Button _remove = new() { Text = "Delete Selected...", AutoSize = true };
    private int _page;
    private string _filter = "";
    private RaceArchiveEntry? Selected => _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].Tag as RaceArchiveEntry;

    internal RaceArchiveBrowser(Func<string, int, RaceArchivePage> list, Func<RaceArchiveEntry, RaceRecoveryFrame> read,
        Func<DateTimeOffset, IReadOnlyList<RaceArchiveEntry>> older, Func<IReadOnlyList<RaceArchiveEntry>, int> delete,
        Action<string> open, RaceExportOptions defaults)
    {
        _list = list; _read = read; _older = older; _delete = delete; _open = open;
        Text = "Race Archives"; StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1040, 530); Size = new Size(1160, 650);
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        FlowLayoutPanel search = new() { Dock = DockStyle.Fill, WrapContents = false };
        search.Controls.Add(new Label { Text = "Race / archive reason", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        search.Controls.Add(_search);
        Button refresh = new() { Text = "Search / Refresh", AutoSize = true };
        refresh.Click += (_, _) => Run(() => { _filter = _search.Text.Trim(); _page = 0; RefreshEntries(); });
        search.Controls.Add(refresh); search.Controls.Add(_previous); search.Controls.Add(_next); search.Controls.Add(_pageLabel);
        _previous.Click += (_, _) => Run(() => { _page--; RefreshEntries(); });
        _next.Click += (_, _) => Run(() => { _page++; RefreshEntries(); });
        layout.Controls.Add(search, 0, 0); layout.Controls.Add(_grid, 0, 1);
        foreach (string column in new[] { "Archived", "Race", "Format", "Status", "Heat", "Demo", "Archive Reason" }) _grid.Columns.Add(column, column);
        _grid.Columns[1].FillWeight = 140; _grid.Columns[6].FillWeight = 160;
        FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.AddRange(new Control[] { _details, _original, _export, _json, _csv, _remove });
        _json.Checked = defaults.ExportJson; _csv.Checked = defaults.ExportCsv;
        _details.Click += (_, _) => Run(ShowDetails);
        _original.Click += (_, _) => Run(() =>
        {
            string path = ArchivedRaceReports.OriginalHtml(_read(RequireSelected()));
            if (!File.Exists(path)) throw new FileNotFoundException("The original HTML report is missing or unavailable. Use Export Copy to regenerate it.", path);
            _open(path);
        });
        _export.Click += (_, _) => Run(ExportCopy);
        _remove.Click += (_, _) => Run(() => ConfirmDelete(new[] { RequireSelected() }));
        _grid.SelectionChanged += (_, _) => UpdateActions();
        layout.Controls.Add(actions, 0, 2);
        FlowLayoutPanel retention = new() { Dock = DockStyle.Fill, WrapContents = false };
        retention.Controls.Add(new Label { Text = "Archived before", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        retention.Controls.Add(_cutoff);
        Button removeOld = new() { Text = "Delete Older Archives...", AutoSize = true };
        removeOld.Click += (_, _) => Run(() => ConfirmDelete(_older(new DateTimeOffset(_cutoff.Value.Date))));
        retention.Controls.Add(removeOld);
        Button close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        retention.Controls.Add(close); CancelButton = close;
        layout.Controls.Add(retention, 0, 3); Controls.Add(layout);
        Shown += (_, _) => Run(RefreshEntries);
    }

    private RaceArchiveEntry RequireSelected() => Selected ?? throw new InvalidOperationException("Select an archived event.");
    private void RefreshEntries()
    {
        RaceArchivePage page = _list(_filter, _page);
        if (page.Entries.Count == 0 && _page > 0) { _page = 0; page = _list(_filter, _page); }
        _grid.Rows.Clear();
        foreach (var entry in page.Entries)
        {
            int row = _grid.Rows.Add(entry.ArchivedLocal, entry.RaceName, entry.Format, entry.Status,
                $"{entry.Heat}/{entry.TotalHeats}", entry.Demo ? "Yes" : "No", entry.Reason);
            _grid.Rows[row].Tag = entry;
        }
        _pageLabel.Text = $"Page {_page + 1} / {Math.Max(1, (page.Count + 49) / 50)} - {page.Count} events";
        _previous.Enabled = _page > 0; _next.Enabled = (_page + 1L) * 50 < page.Count;
        UpdateActions();
    }
    private void UpdateActions()
    {
        _details.Enabled = Selected?.EventId != null; _remove.Enabled = Selected?.EventId != null;
        _export.Enabled = Selected?.EventId != null && Selected.Status.StartsWith("Completed", StringComparison.Ordinal);
        _original.Enabled = _export.Enabled;
    }
    private void ShowDetails()
    {
        var entry = RequireSelected(); var saved = _read(entry);
        StringBuilder text = new();
        text.AppendLine($"{saved.Heat.RaceName}\nEvent: {saved.EventId}\nArchived: {entry.ArchivedLocal}\nReason: {entry.Reason}\nStatus: {entry.Status}\nDemo: {saved.Demo}\nLast saved: {saved.SavedAt.LocalDateTime:g}\n");
        text.AppendLine($"Format: {saved.Heat.Format}\nScheduling: {saved.Heat.Scheduling}\nHeat: {saved.Heat.HeatNumber}/{saved.Heat.TotalHeats}\nState: {saved.Heat.State}\nElapsed: {saved.Heat.ElapsedMilliseconds / 1000.0:0.000}s\nQualifying: {saved.Qualifying.State}\nVoided attempts: {saved.Heat.Reruns?.Length ?? 0}\n");
        text.AppendLine("Lane totals at save (not final standings):");
        foreach (var lane in saved.Lanes.OrderBy(lane => lane.Snapshot.LaneIndex))
            text.AppendLine($"{lane.Snapshot.LaneIndex + 1}: {saved.Heat.Lanes[lane.Snapshot.LaneIndex].Name} - {lane.Snapshot.TotalLapCount} laps");
        text.AppendLine("\nQualifying results:");
        foreach (var result in saved.Heat.Qualifiers.Concat(saved.Qualifying.Results).DistinctBy(item => item.OriginalOrder))
            text.AppendLine($"{result.RacerName}: best {result.BestLapMilliseconds?.ToString() ?? "unknown"} ms" +
                (result.Distance == null ? "" : $", approved {DistanceScoring.Format(result.Distance.TotalHundredths)} laps"));
        using Form viewer = HelpSupport.CreateDocumentViewer("Archived Event Details", text.ToString(), HelpSupport.OpenWeb);
        viewer.ShowDialog(this);
    }
    private void ExportCopy()
    {
        RaceRecoveryFrame frame = _read(RequireSelected());
        ArchivedRaceReports.Create(frame);
        using FolderBrowserDialog folder = new() { Description = "Choose a parent folder for a new archive export copy", UseDescriptionForTitle = true };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        RaceExportPaths paths = ArchivedRaceReports.ExportCopy(frame, folder.SelectedPath, new(_json.Checked, _csv.Checked));
        _open(paths.Html);
    }
    private void ConfirmDelete(IReadOnlyList<RaceArchiveEntry> entries)
    {
        if (entries.Count == 0) { MessageBox.Show(this, "No matching archives.", Text); return; }
        string names = string.Join("\n", entries.Take(5).Select(entry => $"{entry.ArchivedLocal}: {entry.RaceName} ({entry.Status})"));
        if (MessageBox.Show(this, $"Permanently delete {entries.Count} archived event(s)?\n\n{names}\n\nThis includes their saved lap history and audits. Exported files and the active event are not deleted. This cannot be undone.",
            "Delete Race Archives", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _delete(entries); RefreshEntries();
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            System.Text.Json.JsonException or SqliteException or System.ComponentModel.Win32Exception)
        { MessageBox.Show(this, ex.Message, "Race Archives", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
