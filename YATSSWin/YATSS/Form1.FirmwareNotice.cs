namespace YATSS;

public partial class YATSS
{
    private readonly System.Windows.Forms.Timer _firmwareNoticeTimer = new() { Interval = 1500 };
    private readonly HashSet<string> _firmwareNotices = new(StringComparer.Ordinal);
    private IReadOnlyList<ControllerFirmwarePackage> _noticePackages = Array.Empty<ControllerFirmwarePackage>();
    private string? _noticeUnoQVersion;
    private bool _noticeCatalogLoaded, _firmwareNoticeOpen;

    private void InitializeFirmwareNotices()
    {
        _firmwareNoticeTimer.Tick += async (_, _) => await CheckFirmwareNoticeAsync();
        Shown += (_, _) => _firmwareNoticeTimer.Start();
        FormClosed += (_, _) => _firmwareNoticeTimer.Dispose();
    }

    private async Task CheckFirmwareNoticeAsync()
    {
        if (_firmwareNoticeOpen || IsDisposed || !Enabled || ActiveForm != this || OwnedForms.Length != 0 ||
            AppContext.GetData("YATSS.TestDataDirectory") is string || !s.CanOfferFirmwareUpdate) return;
        ControllerIdentity? identity = s.CurrentControllerIdentity;
        if (identity == null) return;
        if (!_noticeCatalogLoaded)
        {
            _noticeCatalogLoaded = true;
            try
            {
                _noticePackages = ControllerFirmwarePackage.LoadBundledPackages();
                string root = Path.Combine(AppContext.BaseDirectory, "Linux", "UNOQ");
                if (Directory.Exists(root))
                {
                    string version = UnoQUpdateClient.Bundle(root).Version;
                    if (version == ControllerFirmwarePackage.BundledFirmwareVersion) _noticeUnoQVersion = version;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or System.Text.Json.JsonException)
            {
                _noticePackages = Array.Empty<ControllerFirmwarePackage>();
                s.Log.Error(ex, "Startup firmware availability check skipped; bundled firmware could not be validated");
                return;
            }
        }
        FirmwareUpdateOffer? offer = FirmwareAvailability.Find(identity, _noticePackages);
        bool localUnoQ = PlatformEnvironment.IsWine && identity.BoardProfile == ControllerFirmwareUpdatePolicy.ArduinoUnoQBoardProfile &&
            identity.FlashCapacityBytes == 2097152 && ControllerEndpoint.TryParseTcp(port, out string host, out int tcpPort) &&
            host is "127.0.0.1" or "localhost" && tcpPort == 45991;
        if (offer == null && localUnoQ && _noticeUnoQVersion != null)
            offer = FirmwareAvailability.Offer(identity, "Arduino UNO Q", _noticeUnoQVersion);
        if (offer == null) return;
        string offeredPort = port;
        if (!_firmwareNotices.Add(offer.Key(identity, offeredPort))) return;
        _firmwareNoticeOpen = true;
        try
        {
            DialogResult answer = MessageBox.Show(this,
                $"New controller firmware is available for {offer.Board}.\n\n" +
                $"Installed: {offer.Installed}\nAvailable: {offer.Available}\n\n" +
                "Open the firmware updater now? You will still confirm the update and safety precautions.\n\n" +
                "Choose No to leave the controller unchanged. File > Update Controller Firmware remains available.",
                "New Firmware Available", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes && !IsDisposed && port == offeredPort &&
                s.CurrentControllerIdentity == identity && s.CanOfferFirmwareUpdate)
                await UpdateControllerFirmwareAsync();
            else if (answer == DialogResult.Yes && !IsDisposed)
                SetStatusMessage("Controller changed or update is not currently safe. Use File > Update Controller Firmware when ready.");
        }
        finally { _firmwareNoticeOpen = false; }
    }
}
