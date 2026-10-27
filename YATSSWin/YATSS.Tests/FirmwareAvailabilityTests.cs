using YATSS;

internal static class FirmwareAvailabilityTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Firmware notice: " + message); }

    internal static void Run()
    {
        Check(FirmwareAvailability.Compare("0.25.0-capture.1", "0.20.0-beta.2-session.1") > 0, "current update exceeds old session firmware");
        Check(FirmwareAvailability.Compare("0.9.10", "0.9.9") > 0 && FirmwareAvailability.Compare("0.10.0", "0.9.99") > 0,
            "versions compare numerically, not lexically");
        Check(FirmwareAvailability.Compare("1.0.0-beta.10", "1.0.0-beta.2") > 0, "numeric prerelease identifiers compare numerically");
        Check(FirmwareAvailability.Compare("1.0.0", "1.0.0-beta.2") > 0 &&
            FirmwareAvailability.Compare("1.0.0-alpha", "1.0.0-1") > 0, "stable/prerelease semantic ordering");
        Check(FirmwareAvailability.Compare("1.0.0+build2", "1.0.0+build1") == 0, "build metadata is not an update");
        foreach (string unknown in new[] { "unknown", "dev", "1.2", "01.2.3", "1.2.3-beta.01", "1.2.3-", "999999999999.0.0" })
            Check(FirmwareAvailability.Compare("1.2.3", unknown) == null, "unparseable installed version must not be guessed: " + unknown);
        var packages = ControllerFirmwarePackage.LoadBundledPackages();
        Check(packages.Count == 4, "four current validated bundles available");
        foreach (var package in packages)
        {
            var manifest = package.Manifest;
            var identity = new ControllerIdentity(5, 8, manifest.BoardProfile, "0.1.0", manifest.FlashCapacityBytes, "123456789ABCDEF1");
            var offer = FirmwareAvailability.Find(identity, packages);
            Check(offer?.Available == manifest.FirmwareVersion && offer.Board == manifest.BoardDisplayName, "exact board/capacity offer");
            Check(FirmwareAvailability.Find(identity with { FirmwareVersion = manifest.FirmwareVersion }, packages) == null,
                "same firmware does not prompt");
            Check(FirmwareAvailability.Find(identity with { FirmwareVersion = "99.0.0" }, packages) == null, "newer installed firmware never prompts downgrade");
            Check(FirmwareAvailability.Find(identity with { FirmwareVersion = "local-test" }, packages) == null, "unknown installed build does not prompt");
            Check(FirmwareAvailability.Find(identity with { FlashCapacityBytes = null }, packages) == null, "unknown capacity does not guess package");
            Check(FirmwareAvailability.Find(identity, new[] { package, package }) == null, "ambiguous packages do not prompt");
            Check(offer!.Key(identity, "COM27") == offer.Key(identity with { SessionId = "123456789ABCDEF2" }, "COM27"),
                "reboot/reconnect does not repeat dismissed version offer");
            Check(offer.Key(identity, "COM27") != offer.Key(identity, "COM28"), "different connected port has separate notice");
        }
        Check(FirmwareAvailability.Find(null, packages) == null && FirmwareAvailability.Find(new(5, 8, "UNKNOWN", "0.1.0", 4194304), packages) == null,
            "no controller or unknown controller does not prompt");
        Check(FirmwareAvailability.Find(new(5, 8, "RASPBERRY_PI_PICO_2_W", "0.1.0", 4194304), packages) == null,
            "Pico has no bundled updater and is not offered ESP32 firmware");
        Check(FirmwareAvailability.Find(new(5, 8, ControllerFirmwarePackage.Esp32C6BoardProfile, "0.1.0", 2097152), packages) == null,
            "wrong flash capacity does not prompt");
        Console.WriteLine("Offline firmware availability, semantic ordering, exact-board/capacity matching and reconnect notice identity passed.");
    }
}
