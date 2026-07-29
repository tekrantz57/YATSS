using YATSS;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

string encodedEdge = LapProtocolParser.EncodeFrame("EDGE:3:42:12345");
LapProtocolMessage edge = LapProtocolParser.Parse(encodedEdge);
Assert(edge.Kind == LapProtocolMessageKind.Edge, "checksummed EDGE should parse");
Assert(edge.Edge is { LaneIndex: 3, Sequence: 42, TimestampMillis: 12345 }, "EDGE fields should parse");

LapProtocolMessage corrupt = LapProtocolParser.Parse("EDGE:3:42:12345*00");
Assert(corrupt.Kind == LapProtocolMessageKind.Invalid, "bad checksum should be rejected");

LapProtocolMessage boot = LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("HELLO:YATSSMC:2:8"));
Assert(boot.Kind == LapProtocolMessageKind.Hello, "HELLO should parse");
Assert(boot.ControllerIdentity is { ProtocolVersion: 2, LaneCount: 8, HasBoardProfile: false },
    "legacy HELLO should retain protocol and lane count without claiming a board identity");

LapProtocolMessage identifiedBoot = LapProtocolParser.Parse(LapProtocolParser.EncodeFrame(
    "HELLO:YATSSMC:3:8:ESP32_C6_DEVKITC1:0.10.0-beta.1-dev"));
Assert(identifiedBoot.ControllerIdentity is
    {
        ProtocolVersion: 3,
        LaneCount: 8,
        BoardProfile: "ESP32_C6_DEVKITC1",
        FirmwareVersion: "0.10.0-beta.1-dev"
    }, "protocol-v3 HELLO should identify board and firmware");

if (string.Equals(
        Environment.GetEnvironmentVariable("YATSS_TEST_OFFICIAL_DOWNLOADS"),
        "1",
        StringComparison.Ordinal))
{
    string downloadTestDirectory = Path.Combine(
        Path.GetTempPath(),
        "YATSS.Tests",
        Guid.NewGuid().ToString("N"));
    try
    {
        string downloadedDfuUtil = await DfuToolProvider.DownloadOfficialDfuUtilAsync(
            localApplicationData: downloadTestDirectory);
        Assert(File.Exists(downloadedDfuUtil) && new FileInfo(downloadedDfuUtil).Length > 0,
            "official Arduino DFU utility should download, verify, and extract");
    }
    finally
    {
        if (Directory.Exists(downloadTestDirectory))
        {
            Directory.Delete(downloadTestDirectory, recursive: true);
        }
    }
}

string firmwarePackageTestDirectory = Path.Combine(
    Path.GetTempPath(),
    "YATSS.Tests",
    Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(firmwarePackageTestDirectory);
    byte[] testImage = Enumerable.Range(0, 1024).Select(value => (byte)value).ToArray();
    string imageName = "test-c6.bin";
    string packagePath = Path.Combine(firmwarePackageTestDirectory, "test.yatssfw");
    ControllerFirmwareManifest manifest = new(
        FormatVersion: ControllerFirmwarePackage.CurrentFormatVersion,
        Product: "YATSSMC",
        FirmwareVersion: "test-version",
        BoardProfile: ControllerFirmwarePackage.Esp32C6BoardProfile,
        BoardDisplayName: "ESP32-C6-DevKitC-1",
        Chip: "esp32c6",
        UploaderBackend: "esptool",
        ArduinoFqbn: "esp32:esp32:esp32c6",
        ArduinoCoreVersion: "test-core",
        ImageFile: imageName,
        ImageSizeBytes: testImage.Length,
        FlashOffset: 0,
        Sha256: Convert.ToHexString(SHA256.HashData(testImage)),
        FlashCapacityBytes: 8 * 1024 * 1024);
    using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
    {
        using (Stream manifestStream = archive.CreateEntry("manifest.json").Open())
        {
            JsonSerializer.Serialize(manifestStream, manifest);
        }
        using Stream imageStream = archive.CreateEntry(imageName).Open();
        imageStream.Write(testImage);
    }

    ControllerFirmwarePackage loadedPackage = ControllerFirmwarePackage.Load(packagePath);
    Assert(loadedPackage.ImageBytes.SequenceEqual(testImage),
        "firmware package should retain an image that matches its manifest hash");
    Assert(loadedPackage.Matches(identifiedBoot.ControllerIdentity!),
        "firmware package should match the controller board profile");

    IReadOnlyList<string> flashArguments = Esp32C6FirmwareFlasher.CreateFlashArguments("COM9", "firmware.bin");
    Assert(flashArguments.Contains("esp32c6") && flashArguments.TakeLast(2).SequenceEqual(new[] { "0x0", "firmware.bin" }),
        "C6 flasher should enforce the chip and merged-image offset");
    Assert(EspToolProvider.GetCachedEspToolPath("C:\\LocalData").EndsWith(
        $"YATSS\\Tools\\esptool\\{EspToolProvider.OfficialVersion}\\esptool.exe",
        StringComparison.OrdinalIgnoreCase),
        "official uploader should be cached outside the YATSS installation");

    using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
    {
        ZipArchiveEntry imageEntry = archive.GetEntry(imageName)!;
        imageEntry.Delete();
        using Stream replacement = archive.CreateEntry(imageName).Open();
        replacement.Write(new byte[testImage.Length]);
    }
    bool tamperedPackageRejected = false;
    try
    {
        _ = ControllerFirmwarePackage.Load(packagePath);
    }
    catch (InvalidDataException)
    {
        tamperedPackageRejected = true;
    }
    Assert(tamperedPackageRejected, "firmware package should reject an image that fails SHA-256 validation");

    byte[] nanoImage = Enumerable.Range(0, 2048).Select(value => (byte)(value * 3)).ToArray();
    string nanoImageName = "test-nano.bin";
    string nanoPackagePath = Path.Combine(firmwarePackageTestDirectory, "test-nano.yatssfw");
    ControllerFirmwareManifest nanoManifest = new(
        FormatVersion: ControllerFirmwarePackage.CurrentFormatVersion,
        Product: "YATSSMC",
        FirmwareVersion: "test-version",
        BoardProfile: ControllerFirmwarePackage.ArduinoNanoEsp32BoardProfile,
        BoardDisplayName: "Arduino Nano ESP32",
        Chip: "esp32s3",
        UploaderBackend: "dfu-util",
        ArduinoFqbn: "arduino:esp32:nano_nora",
        ArduinoCoreVersion: "test-core",
        ImageFile: nanoImageName,
        ImageSizeBytes: nanoImage.Length,
        FlashOffset: 0,
        Sha256: Convert.ToHexString(SHA256.HashData(nanoImage)),
        FlashCapacityBytes: 16 * 1024 * 1024,
        UsbVendorId: "2341",
        UsbProductId: "0070");
    using (ZipArchive archive = ZipFile.Open(nanoPackagePath, ZipArchiveMode.Create))
    {
        using (Stream manifestStream = archive.CreateEntry("manifest.json").Open())
        {
            JsonSerializer.Serialize(manifestStream, nanoManifest);
        }
        using Stream imageStream = archive.CreateEntry(nanoImageName).Open();
        imageStream.Write(nanoImage);
    }
    ControllerFirmwarePackage loadedNanoPackage = ControllerFirmwarePackage.Load(nanoPackagePath);
    Assert(loadedNanoPackage.Manifest.BoardProfile == ControllerFirmwarePackage.ArduinoNanoEsp32BoardProfile,
        "firmware package should accept the Arduino Nano ESP32 DFU profile");
    IReadOnlyList<string> nanoArguments = ArduinoNanoFirmwareFlasher.CreateFlashArguments(
        loadedNanoPackage.Manifest,
        "nano.bin");
    Assert(nanoArguments.SequenceEqual(new[] { "--device", "2341:0070", "-D", "nano.bin", "-Q" }),
        "Nano flasher should use Arduino's VID/PID and quiet-reset DFU upload recipe");
    Assert(DfuToolProvider.GetCachedDfuUtilPath("C:\\LocalData").EndsWith(
        $"YATSS\\Tools\\dfu-util\\{DfuToolProvider.OfficialVersion}\\dfu-util.exe",
        StringComparison.OrdinalIgnoreCase),
        "Arduino DFU utility should be cached outside the YATSS installation");
}
finally
{
    if (Directory.Exists(firmwarePackageTestDirectory))
    {
        Directory.Delete(firmwarePackageTestDirectory, recursive: true);
    }
}

LapProtocolMessage heartbeat = LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("HEARTBEAT:12345"));
Assert(heartbeat.Kind == LapProtocolMessageKind.Heartbeat, "HEARTBEAT should parse");
Assert(heartbeat.ControllerTimestampMillis == 12345, "HEARTBEAT timestamp should parse");

LapProtocolMessage watchdog = LapProtocolParser.Parse(
    LapProtocolParser.EncodeFrame("ERR:WINDOWS_WATCHDOG:23456"));
Assert(watchdog.Kind == LapProtocolMessageKind.Error, "watchdog trip should parse as a controller error");
Assert(watchdog.ControllerTimestampMillis == 23456, "watchdog trip should retain its controller timestamp");

LapProtocolMessage diagnosticStatus = LapProtocolParser.Parse(
    LapProtocolParser.EncodeFrame("DIAG:STATUS:05:A3:1800:2:12345"));
Assert(diagnosticStatus.Kind == LapProtocolMessageKind.Diagnostic, "diagnostic status should parse");
Assert(
    diagnosticStatus.Diagnostic is ControllerDiagnosticStatus
    {
        SensorActiveMask: 0x05,
        TrackPowerEnabledMask: 0xA3,
        DebounceMilliseconds: 1800,
        DroppedEvents: 2,
        TimestampMillis: 12345
    },
    "diagnostic status fields should parse");

LapProtocolMessage diagnosticSensor = LapProtocolParser.Parse(
    LapProtocolParser.EncodeFrame("DIAG:SENSOR:3:ACTIVE:7:2:12500"));
Assert(
    diagnosticSensor.Diagnostic is ControllerDiagnosticSensor
    {
        LaneIndex: 3,
        Active: true,
        TransitionCount: 7,
        AcceptedEdgeCount: 2,
        TimestampMillis: 12500
    },
    "diagnostic sensor fields should parse");

LapProtocolMessage diagnosticRelay = LapProtocolParser.Parse(
    LapProtocolParser.EncodeFrame("DIAG:RELAY:2:PULSING:FB:13000"));
Assert(
    diagnosticRelay.Diagnostic is ControllerDiagnosticRelay
    {
        LaneIndex: 2,
        State: "PULSING",
        TrackPowerEnabledMask: 0xFB,
        TimestampMillis: 13000
    },
    "diagnostic relay fields should parse");

LapProtocolMessage diagnosticSession = LapProtocolParser.Parse(
    LapProtocolParser.EncodeFrame("DIAG:SESSION:STOPPED:TIMEOUT:14000"));
Assert(
    diagnosticSession.Diagnostic is ControllerDiagnosticSession
    {
        State: "STOPPED",
        Reason: "TIMEOUT",
        TimestampMillis: 14000
    },
    "diagnostic session fields should parse");

Random demoRandom = new(1979);
int[] demoLanePaces = DemoLapTiming.CreateLanePaces(demoRandom);
Assert(demoLanePaces.Length == LapProtocolParser.LaneCount,
    "demo timing should create one reference pace per lane");
Assert(demoLanePaces.Distinct().Count() == LapProtocolParser.LaneCount,
    "demo lane reference paces should be shuffled without duplication");
DemoLapTiming demoTiming = new();
demoTiming.ConfigureRacers(new[] { "Ada", "Grace" });
int adaPace = demoTiming.GetReferencePaceMilliseconds(0, demoLanePaces, "Ada");
Assert(demoLanePaces.Contains(adaPace),
    "configured demo racers should receive a valid reference pace");
Assert(demoTiming.GetReferencePaceMilliseconds(3, demoLanePaces, "") == demoLanePaces[3],
    "practice demo timing should fall back to the lane pace");
for (int sample = 0; sample < 100; sample++)
{
    int interval = DemoLapTiming.GetLapIntervalMilliseconds(
        demoRandom,
        referenceBaseLapMilliseconds: 4300,
        trackLengthFeet: 155,
        configuredMinimumLapMilliseconds: 1800);
    Assert(interval is >= 4200 and <= 6500,
        "demo lap intervals should remain inside reference-track bounds");
}
Assert(DemoLapTiming.GetLapIntervalMilliseconds(
        demoRandom,
        referenceBaseLapMilliseconds: 4300,
        trackLengthFeet: 155,
        configuredMinimumLapMilliseconds: 15000) == 15000,
    "configured minimum lap time should constrain demo timing");
int demoBaseline = DemoLapTiming.GetFirstBaselineMilliseconds(
    demoRandom,
    referenceBaseLapMilliseconds: 4300,
    trackLengthFeet: 155,
    configuredMinimumLapMilliseconds: 1800);
Assert(demoBaseline is >= 1400 and <= 2167,
    "demo first baseline should represent roughly one-third of a lap");

LapProtocolMessage badDiagnosticMask = LapProtocolParser.Parse(
    LapProtocolParser.EncodeFrame("DIAG:STATUS:5:A3:1800:2:12345"));
Assert(badDiagnosticMask.Kind == LapProtocolMessageKind.Invalid, "diagnostic masks require two hex digits");

LapProtocolMessage badLane = LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("EDGE:8:1:100"));
Assert(badLane.Kind == LapProtocolMessageKind.Invalid, "Lane 8 should be rejected");

LapProtocolMessage missingChecksum = LapProtocolParser.Parse("EDGE:2:1:10000");
Assert(missingChecksum.Kind == LapProtocolMessageKind.Invalid, "checksum-free frames should be rejected");

LapProtocolMessage obsoleteTwoPart = LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("2:10000"));
Assert(obsoleteTwoPart.Kind == LapProtocolMessageKind.Invalid, "two-part edge frames should be rejected");

LapProtocolMessage obsoleteThreePart = LapProtocolParser.Parse(LapProtocolParser.EncodeFrame("1:7:12500"));
Assert(obsoleteThreePart.Kind == LapProtocolMessageKind.Invalid, "three-part edge frames should be rejected");

LapRace race = new(new LapRaceOptions(1000, 600000, 155.0));
LapUpdate started = race.Process(new LapEdge(0, 1, 1000));
Assert(started.Kind == LapUpdateKind.Started, "first edge should establish baseline");

LapUpdate duplicate = race.Process(new LapEdge(0, 2, 1500));
Assert(duplicate.Kind == LapUpdateKind.TooFast, "short edge should be rejected as too fast");
Assert(duplicate.LapMilliseconds == 500, "too-fast update should report ignored lap duration");

LapUpdate counted = race.Process(new LapEdge(0, 3, 3500));
Assert(counted.Kind == LapUpdateKind.Counted, "valid edge should count lap");
Assert(counted.LapMilliseconds == 2500, "lap duration should be computed from last accepted edge");
Assert(race.GetLane(0).getCount() == 1, "lane should have one counted lap");

LapRace rawLockoutRace = new(new LapRaceOptions(1000, 600000, 155.0, RawSensorLockoutMilliseconds: 500));
Assert(rawLockoutRace.Process(new LapEdge(0, 1, 1000)).Kind == LapUpdateKind.Started, "raw lockout race should start");
LapUpdate rawIgnored = rawLockoutRace.Process(new LapEdge(0, 2, 1300));
Assert(rawIgnored.Kind == LapUpdateKind.RawIgnored, "raw lockout should reject rapid raw edges before lap validation");
Assert(rawIgnored.LapMilliseconds == 300, "raw lockout should report raw edge duration");
LapUpdate rawLockoutCounted = rawLockoutRace.Process(new LapEdge(0, 3, 2600));
Assert(rawLockoutCounted.Kind == LapUpdateKind.Counted, "edge after raw lockout and minimum lap should count");
Assert(rawLockoutCounted.LapMilliseconds == 1600, "counted lap should still measure from the last accepted lap baseline");

LapRace hundredFootRace = new(new LapRaceOptions(1000, 600000, 100.0));
Assert(
    Math.Abs(hundredFootRace.CalculateMilesPerHour(10000) - 6.8166325835) < 0.000001,
    "MPH should use configured track length");
Assert(race.AdjustLapCount(0, 1) == 2, "manual add should increase lap count");
Assert(race.AdjustLapCount(0, -1) == 1, "manual subtract should decrease lap count");
Assert(race.AdjustLapCount(0, -5) == 0, "manual subtract should not create negative lap count");

race.ResetLane(0);
Assert(race.GetLane(0).getCount() == 0, "lane reset should clear lap count");
Assert(race.Process(new LapEdge(0, 4, 5000)).Kind == LapUpdateKind.Started, "lane reset should establish a fresh baseline");

LapUpdate missed = race.Process(new LapEdge(0, 5, 6000));
Assert(missed.Kind == LapUpdateKind.Counted, "post-reset next valid edge should count without a sequence gap");

LapUpdate stale = race.Process(new LapEdge(0, 4, 9000));
Assert(stale.Kind == LapUpdateKind.Duplicate, "stale sequence should be rejected");
Assert(race.GetLane(0).getCount() == 1, "stale sequence should not count a lap");

LapRace ineligibleBestRace = new(new LapRaceOptions(1000, 600000, 155.0));
Assert(ineligibleBestRace.Process(new LapEdge(0, 1, 1000)).Kind == LapUpdateKind.Started, "ineligible best race should start");
LapUpdate ineligibleBest = ineligibleBestRace.Process(new LapEdge(0, 2, 3000), countFirstEdgeAsLap: false, fastestLapEligible: false);
Assert(ineligibleBest.Kind == LapUpdateKind.Counted, "ineligible best lap should still count");
Assert(ineligibleBestRace.GetLane(0).getCount() == 1, "ineligible best lap should increment count");
Assert(ineligibleBestRace.GetLane(0).best_time == int.MaxValue, "ineligible best lap should not set best time");

LapRace carriedCountRace = new(new LapRaceOptions(1000, 600000, 155.0));
carriedCountRace.ResetTimingForHeat(new[] { 4, 0, 0, 0, 0, 0, 0, 0 });
LapUpdate countedFirstEdge = carriedCountRace.Process(new LapEdge(0, 1, 1000), countFirstEdgeAsLap: true, fastestLapEligible: false);
Assert(countedFirstEdge.Kind == LapUpdateKind.Counted, "first edge can count in successive heats");
Assert(!countedFirstEdge.LapMilliseconds.HasValue, "first counted edge should not have a timed lap");
Assert(carriedCountRace.GetLane(0).getCount() == 5, "first counted edge should increment carried count");
Assert(carriedCountRace.GetLane(0).getMedian() == 0, "first counted edge should not affect timing samples");

LapRace timedFirstLapRace = new(new LapRaceOptions(1000, 600000, 155.0));
timedFirstLapRace.ResetTimingForHeat(new[] { 4, 0, 0, 0, 0, 0, 0, 0 });
LapUpdate timedFirstLap = timedFirstLapRace.Process(
    new LapEdge(0, 1, 61000),
    countFirstEdgeAsLap: true,
    fastestLapEligible: false,
    firstLapMilliseconds: 1000);
Assert(timedFirstLap.LapMilliseconds == 1000, "successive heat first lap should report heat-start timing");
Assert(timedFirstLapRace.GetLane(0).getCount() == 5, "timed first lap should increment carried count");
Assert(timedFirstLapRace.GetLane(0).getMedian() == 1000, "timed first lap should contribute to median");
Assert(timedFirstLapRace.GetLane(0).best_time == int.MaxValue, "timed first lap should be excluded from fastest lap");
LapRaceLaneSnapshot timedFirstSnapshot = timedFirstLapRace.GetLaneSnapshots()[0];
Assert(timedFirstSnapshot.Laps.Count == 1, "lane snapshots should retain each counted crossing");
Assert(timedFirstSnapshot.Laps[0].LapMilliseconds == 1000, "lane snapshots should retain lap duration");
Assert(!timedFirstSnapshot.Laps[0].FastestLapEligible, "lane snapshots should retain fastest-lap eligibility");
Assert(timedFirstSnapshot.Laps[0].TimestampMilliseconds == 61000, "lane snapshots should retain crossing time");

LapRace wrapRace = new(new LapRaceOptions(1000, 600000, 155.0));
Assert(wrapRace.Process(new LapEdge(0, uint.MaxValue, uint.MaxValue - 500)).Kind == LapUpdateKind.Started, "wrap race should start");
LapUpdate wrappedTime = wrapRace.Process(new LapEdge(0, 0, 1500));
Assert(wrappedTime.Kind == LapUpdateKind.Counted, "wrapped sequence and timestamp should count");
Assert(wrappedTime.LapMilliseconds == 2001, "timestamp wrap should preserve elapsed milliseconds");

HeatRaceController heat = new();
string[] racers = { "R0", "R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8" };
heat.Configure(1, 15, racers);
Assert(heat.State == HeatRaceState.Ready, "configured heat should be ready");
Assert(heat.BetweenHeatsSeconds == 15, "between-heats seconds should be configured");
Assert(heat.HeatNumber == 1, "configured heat should start at heat 1");
HeatRaceSnapshot firstHeat = heat.GetSnapshot(0);
Assert(firstHeat.LaneRacers[0] == "R0", "first heat should assign first racer to red");
Assert(firstHeat.LaneRacers[1] == "R1", "first heat should assign second racer to white");
Assert(firstHeat.LaneRacers[2] == "R2", "first heat should assign third racer to green");
Assert(firstHeat.LaneRacers[3] == "R3", "first heat should assign fourth racer to orange");
Assert(firstHeat.LaneRacers[4] == "R4", "first heat should assign fifth racer to blue");
Assert(firstHeat.LaneRacers[5] == "R5", "first heat should assign sixth racer to yellow");
Assert(firstHeat.LaneRacers[6] == "R6", "first heat should assign seventh racer to purple");
Assert(firstHeat.LaneRacers[7] == "R7", "first heat should assign eighth racer to black");
Assert(firstHeat.OnDeckRacer == "R8", "ninth racer should be on deck");
Assert(heat.Start(1000), "ready heat should start");
Assert(heat.PrepareEdge(new LapEdge(0, 1, 2000)).Edge.TimestampMillis == 1000, "running heat should adjust edge to active time");
Assert(heat.Pause(10000), "running heat should pause");
Assert(heat.Resume(20000), "paused heat should resume");
HeatRaceEdgeDecision adjustedAfterPause = heat.PrepareEdge(new LapEdge(0, 2, 21000));
Assert(adjustedAfterPause.Edge.TimestampMillis == 10000, "heat adjustment should subtract paused time");
Assert(!heat.IsExpired(70999), "heat should not expire before configured active time");
Assert(heat.IsExpired(71000), "heat should expire at configured active time");
Assert(heat.Complete(), "expired heat should complete");
Assert(!heat.PrepareEdge(new LapEdge(0, 9, 72000)).ShouldProcess, "edges between heats should be ignored");
Assert(heat.PrepareNextHeat(new[] { 10, 11, 12, 13, 14, 15, 16, 17 }), "completed heat should prepare next heat");
Assert(heat.HeatNumber == 2, "next heat should be heat 2");
HeatRaceSnapshot secondHeat = heat.GetSnapshot(80000);
Assert(secondHeat.LaneRacers[0] == "R8", "waiting racer should enter on red");
Assert(secondHeat.LaneRacers[2] == "R0", "red racer should rotate to green");
Assert(secondHeat.LaneRacers[4] == "R2", "green racer should rotate to blue");
Assert(secondHeat.LaneRacers[6] == "R4", "blue racer should rotate to purple");
Assert(secondHeat.LaneRacers[7] == "R6", "purple racer should rotate to black");
Assert(secondHeat.LaneRacers[5] == "R7", "black racer should rotate to yellow");
Assert(secondHeat.LaneRacers[3] == "R5", "yellow racer should rotate to orange");
Assert(secondHeat.LaneRacers[1] == "R3", "orange racer should rotate to white");
Assert(secondHeat.OnDeckRacer == "R1", "white racer should rotate out to on deck");
Assert(secondHeat.LaneLapCounts[2] == 10, "red racer's lap total should rotate to green");
Assert(secondHeat.LaneLapCounts[1] == 13, "orange racer's lap total should rotate to white");

HeatRaceController shortFieldHeat = new();
shortFieldHeat.Configure(1, 0, new[] { "A", "B", "C" });
HeatRaceSnapshot shortField = shortFieldHeat.GetSnapshot(0);
Assert(shortField.LaneRacers[0] == "A", "three racers should start red first");
Assert(shortField.LaneRacers[1] == "B", "three racers should start white second");
Assert(shortField.LaneRacers[2] == "C", "three racers should start green third");
Assert(shortFieldHeat.GetOccupiedLaneMask() == 0x07, "short field should power only occupied lanes");
Assert(shortFieldHeat.Start(100), "short field heat should start");
Assert(shortFieldHeat.PrepareEdge(new LapEdge(4, 1, 200)).ShouldProcess == false, "unoccupied heat lane should be ignored");
Assert(shortFieldHeat.PrepareEdge(new LapEdge(0, 1, 200)).ShouldProcess, "occupied heat lane should process");
Assert(shortFieldHeat.Complete(), "short field heat should complete");
Assert(shortFieldHeat.PrepareNextHeat(new[] { 1, 2, 3, 0, 0, 0, 0, 0 }), "short field should prepare its next heat");
Assert(shortFieldHeat.GetOccupiedLaneMask() == 0x15, "powered lanes should follow racer rotation");
Assert(heat.Start(80000), "next heat should start");
HeatRaceEdgeDecision secondHeatFirstEdge = heat.PrepareEdge(new LapEdge(0, 3, 81000));
Assert(secondHeatFirstEdge.Edge.TimestampMillis == 61000, "second heat adjusted time should continue after heat 1");
Assert(secondHeatFirstEdge.CountFirstEdgeAsLap, "first lane edge after heat 1 should count as a lap");
Assert(!secondHeatFirstEdge.FastestLapEligible, "first lane edge after heat 1 should not be fastest eligible");
Assert(secondHeatFirstEdge.FirstLapMilliseconds == 1000, "successive heat first lap should use active time since heat start");

HeatRaceController staleTimestampHeat = new();
staleTimestampHeat.Configure(1, 0, new[] { "A", "B" });
Assert(staleTimestampHeat.Start(10000), "stale timestamp heat should start");
Assert(!staleTimestampHeat.IsExpired(9000), "older timestamp should not expire a running heat");
Assert(staleTimestampHeat.GetRemaining(9000) == TimeSpan.FromMinutes(1), "older timestamp should not consume heat time");

HeatRaceController rolloverHeat = new();
rolloverHeat.Configure(1, 0, new[] { "A", "B" });
Assert(rolloverHeat.Start(uint.MaxValue - 499), "rollover heat should start near the controller timestamp boundary");
Assert(
    rolloverHeat.GetRemaining(500) == TimeSpan.FromMilliseconds(59000),
    "heat timing should continue across controller timestamp rollover");

HeatRaceController enduroHeat = new();
enduroHeat.Configure(HeatRaceController.MaximumHeatLengthMinutes, 0, new[] { "A", "B" });
Assert(enduroHeat.HeatLengthMinutes == 1440, "heat race should allow a 24-hour heat");
Assert(enduroHeat.Start(1000), "24-hour heat should start");
Assert(!enduroHeat.IsExpired(86400999), "24-hour heat should not expire one millisecond early");
Assert(enduroHeat.IsExpired(86401000), "24-hour heat should expire at exactly 24 hours");

HeatRaceController overlongHeat = new();
overlongHeat.Configure(HeatRaceController.MaximumHeatLengthMinutes + 1, 0, new[] { "A", "B" });
Assert(overlongHeat.HeatLengthMinutes == 1440, "heat length should clamp to the supported 24-hour maximum");

System.Reflection.MethodInfo formatClock = typeof(HeatRaceController).Assembly
    .GetType("YATSS.YATSS")!
    .GetMethod("FormatClock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
Assert(
    (string)formatClock.Invoke(null, new object[] { TimeSpan.FromHours(24) })! == "24:00:00",
    "24-hour heat should display 24:00:00 instead of wrapping to zero hours");

HeatRaceController fourLaneHeat = new();
fourLaneHeat.Configure(1, 0, new[] { "A", "B", "C", "D", "E" }, activeLaneCount: 4);
Assert(fourLaneHeat.TotalHeats == 5, "four-lane race with five racers should run five heats");
HeatRaceSnapshot fourLaneFirstHeat = fourLaneHeat.GetSnapshot(0);
Assert(fourLaneFirstHeat.LaneRacers.Take(4).SequenceEqual(new[] { "A", "B", "C", "D" }), "four-lane first heat should fill its four physical lanes");
Assert(fourLaneHeat.GetOccupiedLaneMask() == 0x0F, "four-lane heat should power only its four physical lanes");
Assert(fourLaneFirstHeat.OnDeckRacer == "E", "fifth racer should wait in a four-lane race");
Assert(fourLaneHeat.Start(0), "four-lane first heat should start");
Assert(fourLaneHeat.Complete(), "four-lane first heat should complete");
Assert(fourLaneHeat.PrepareNextHeat(new[] { 1, 2, 3, 4, 0, 0, 0, 0 }), "four-lane second heat should prepare");
HeatRaceSnapshot fourLaneSecondHeat = fourLaneHeat.GetSnapshot(0);
Assert(fourLaneSecondHeat.LaneRacers[0] == "E", "waiting racer should enter on red");
Assert(fourLaneSecondHeat.LaneRacers[2] == "A", "red racer should rotate to green");
Assert(fourLaneSecondHeat.LaneRacers[3] == "C", "green racer should rotate to orange");
Assert(fourLaneSecondHeat.LaneRacers[1] == "D", "orange racer should rotate to white");
Assert(fourLaneSecondHeat.OnDeckRacer == "B", "white racer should rotate out");

HeatRaceController largeFieldHeat = new();
string[] largeFieldRacers = Enumerable.Range(1, 10).Select(index => $"R{index}").ToArray();
largeFieldHeat.Configure(1, 0, largeFieldRacers, activeLaneCount: 8);
Assert(largeFieldHeat.TotalHeats == 10, "ten-racer race should run ten heats");
Assert(largeFieldHeat.CreateReport().LaneNames.Count == 8, "report should keep lane metadata to physical lanes");
Dictionary<string, int> expectedLargeFieldTotals = new(StringComparer.OrdinalIgnoreCase);
foreach (string racer in largeFieldRacers)
{
    expectedLargeFieldTotals[racer] = 0;
}

for (int heatNumber = 1; heatNumber <= largeFieldHeat.TotalHeats; heatNumber++)
{
    uint heatTimestamp = (uint)(heatNumber * 100000);
    Assert(largeFieldHeat.Start(heatTimestamp), $"large field heat {heatNumber} should start");
    HeatRaceSnapshot snapshot = largeFieldHeat.GetSnapshot(heatTimestamp);
    int[] laneLapCounts = snapshot.LaneLapCounts.ToArray();
    for (int lane = 0; lane < snapshot.LaneRacers.Count; lane++)
    {
        string racer = snapshot.LaneRacers[lane];
        if (string.IsNullOrWhiteSpace(racer))
        {
            continue;
        }

        Assert(laneLapCounts[lane] == expectedLargeFieldTotals[racer], $"{racer} lap total should appear when rotating into lane {lane + 1}");
        int heatLaps = heatNumber + lane + 1;
        laneLapCounts[lane] += heatLaps;
        expectedLargeFieldTotals[racer] += heatLaps;
    }

    largeFieldHeat.RecordHeatResults(laneLapCounts, new int?[LapProtocolParser.LaneCount]);
    if (heatNumber < largeFieldHeat.TotalHeats)
    {
        Assert(largeFieldHeat.Complete(), $"large field heat {heatNumber} should complete");
        Assert(largeFieldHeat.PrepareNextHeat(laneLapCounts), $"large field heat {heatNumber + 1} should prepare");
    }
}

HeatRaceReport largeFieldReport = largeFieldHeat.CreateReport();
foreach (string racer in largeFieldRacers)
{
    HeatRaceRacerReport racerReport = largeFieldReport.Racers.Single(result => result.RacerName == racer);
    Assert(racerReport.TotalLaps == expectedLargeFieldTotals[racer], $"{racer} total laps should follow rotations");
}

LaneConfiguration[] customLanes = LaneConfiguration.CreateDefaults().ToArray();
customLanes[0] = new LaneConfiguration("Aqua", System.Drawing.Color.Aqua.ToArgb());
customLanes[1] = new LaneConfiguration("Pink", System.Drawing.Color.HotPink.ToArgb());
HeatRaceController customLaneHeat = new();
customLaneHeat.Configure(1, 0, new[] { "A", "B" }, activeLaneCount: 4, laneConfigurations: customLanes);
HeatRaceReport customLaneReport = customLaneHeat.CreateReport();
Assert(customLaneReport.LaneNames[0] == "Aqua", "report should use configured lane names");
Assert(customLaneReport.LaneColorArgb[1] == System.Drawing.Color.HotPink.ToArgb(), "report should use configured lane colors");

HeatRaceController reportHeat = new();
reportHeat.Configure(
    1,
    0,
    new[] { "Ada", "Grace" },
    raceName: "Thursday Night",
    trackLengthFeet: 123.5,
    qualifyingResults: new[]
    {
        new QualifyingResult("Ada", 0, 1900)
        {
            LaneIndex = 2,
            ConfiguredDurationSeconds = 30,
            ElapsedMilliseconds = 30000,
            Laps = new[]
            {
                new QualifyingLapRecord(1, 2100, 4500),
                new QualifyingLapRecord(2, 1900, 6400)
            }
        },
        new QualifyingResult("Grace", 1, 2100)
    });
Assert(reportHeat.Start(0), "report heat should start");
Assert(reportHeat.Pause(1000), "paused heat should allow lap adjustment");
Assert(reportHeat.CanAdjustLapCounts, "paused heat should allow manual lap adjustment");
LapRace reportLapRace = new();
Assert(reportLapRace.Process(new LapEdge(0, 1, 0)).Kind == LapUpdateKind.Started, "report lane 1 should establish baseline");
Assert(reportLapRace.Process(new LapEdge(0, 2, 2100)).Kind == LapUpdateKind.Counted, "report lane 1 first lap should count");
Assert(reportLapRace.Process(new LapEdge(0, 3, 4200)).Kind == LapUpdateKind.Counted, "report lane 1 second lap should count");
Assert(reportLapRace.Process(new LapEdge(1, 1, 0)).Kind == LapUpdateKind.Started, "report lane 2 should establish baseline");
Assert(reportLapRace.Process(new LapEdge(1, 2, 2200)).Kind == LapUpdateKind.Counted, "report lane 2 lap should count");
Assert(reportLapRace.AdjustLapCount(0, 3) == 5, "report lane 1 manual laps should be applied");
Assert(reportLapRace.AdjustLapCount(1, 3) == 4, "report lane 2 manual laps should be applied");
reportHeat.RecordManualLapAdjustment(0, 3, 5);
reportHeat.RecordManualLapAdjustment(1, 3, 4);
reportHeat.RecordHeatResults(reportLapRace.GetLaneSnapshots());
HeatRaceReport report = reportHeat.CreateReport();
Assert(report.RaceName == "Thursday Night", "report should include race name");
Assert(report.TrackLengthFeet == 123.5, "report should include configured track length");
Assert(report.QualifyingResults[0].RacerName == "Ada", "report should include qualifying order");
Assert(report.HeatLengthMinutes == 1, "report should include heat length");
Assert(report.BetweenHeatsSeconds == 0, "report should include between-heat seconds");
Assert(report.Notes.Contains("Manual lap adjustments", StringComparison.OrdinalIgnoreCase), "report should include manual adjustment note");
Assert(report.Racers[0].RacerName == "Ada", "report should sort finish order by total laps");
Assert(report.Racers[0].TotalLaps == 5, "report should include total laps");
Assert(report.Racers[0].HeatLaps[0] == 5, "report should include heat laps");
Assert(report.Racers[0].BestLapByLaneMilliseconds[0] == 2100, "report should include fast lap by lane");
Assert(report.Laps.Count == 3, "report should retain every accepted heat lap");
Assert(report.Laps[0].RaceElapsedMilliseconds == 2100, "report lap should retain race elapsed time");
Assert(report.ManualAdjustments.Count == 2, "report should retain each manual lap adjustment");
Assert(report.QualifyingResults[0].Laps.Count == 2, "report should retain qualifying lap history");

string exportDirectory = Path.Combine(Path.GetTempPath(), "YATSS.Tests", Guid.NewGuid().ToString("N"));
try
{
    RaceExportPaths exports = RaceArchiveWriter.Write(report, exportDirectory);
    Assert(File.Exists(exports.Html), "HTML race report should be exported");
    Assert(File.Exists(exports.Json), "JSON race archive should be exported");
    Assert(File.Exists(exports.ResultsCsv), "results CSV should be exported");
    Assert(File.Exists(exports.LapsCsv), "laps CSV should be exported");
    Assert(File.Exists(exports.QualifyingCsv), "qualifying CSV should be exported");
    Assert(File.Exists(exports.AdjustmentsCsv), "adjustments CSV should be exported");

    using JsonDocument archive = JsonDocument.Parse(File.ReadAllText(exports.Json!));
    Assert(
        archive.RootElement.GetProperty("schemaVersion").GetInt32() == RaceArchiveWriter.CurrentSchemaVersion,
        "JSON archive should declare its schema version");
    Assert(
        archive.RootElement.GetProperty("race").GetProperty("laps").GetArrayLength() == 3,
        "JSON archive should contain accepted heat laps");
    Assert(
        File.ReadAllText(exports.QualifyingCsv!).Contains("1900", StringComparison.Ordinal),
        "qualifying CSV should contain individual qualifying laps");
    Assert(
        File.ReadAllText(exports.AdjustmentsCsv!).Contains(",3,5,", StringComparison.Ordinal),
        "adjustments CSV should contain manual correction details");

    string htmlOnlyDirectory = Path.Combine(exportDirectory, "html-only");
    RaceExportPaths htmlOnly = RaceArchiveWriter.Write(
        report,
        htmlOnlyDirectory,
        new RaceExportOptions(ExportJson: false, ExportCsv: false));
    Assert(File.Exists(htmlOnly.Html), "HTML report should always be exported");
    Assert(htmlOnly.Json == null, "disabled JSON export should not return a path");
    Assert(htmlOnly.ResultsCsv == null && htmlOnly.LapsCsv == null &&
        htmlOnly.QualifyingCsv == null && htmlOnly.AdjustmentsCsv == null,
        "disabled CSV export should not return paths");
    Assert(Directory.GetFiles(htmlOnlyDirectory).Length == 1, "HTML-only export should create only one file");

    string jsonOnlyDirectory = Path.Combine(exportDirectory, "json-only");
    RaceExportPaths jsonOnly = RaceArchiveWriter.Write(
        report,
        jsonOnlyDirectory,
        new RaceExportOptions(ExportJson: true, ExportCsv: false));
    Assert(File.Exists(jsonOnly.Html) && File.Exists(jsonOnly.Json), "JSON-only option should create HTML and JSON");
    Assert(jsonOnly.ResultsCsv == null && Directory.GetFiles(jsonOnlyDirectory).Length == 2,
        "JSON-only option should not create CSV files");

    string csvOnlyDirectory = Path.Combine(exportDirectory, "csv-only");
    RaceExportPaths csvOnly = RaceArchiveWriter.Write(
        report,
        csvOnlyDirectory,
        new RaceExportOptions(ExportJson: false, ExportCsv: true));
    Assert(csvOnly.Json == null, "CSV-only option should not create JSON");
    Assert(File.Exists(csvOnly.Html) && File.Exists(csvOnly.ResultsCsv) &&
        File.Exists(csvOnly.LapsCsv) && File.Exists(csvOnly.QualifyingCsv) &&
        File.Exists(csvOnly.AdjustmentsCsv),
        "CSV-only option should create HTML and all CSV tables");
    Assert(Directory.GetFiles(csvOnlyDirectory).Length == 5, "CSV-only option should create five files");

    Assert(
        File.ReadAllText(exports.ResultsCsv!).Contains("Thursday Night", StringComparison.Ordinal),
        "results CSV should include the race name");
    Assert(
        File.ReadAllText(exports.Html).Contains("Manual Lap Adjustments", StringComparison.Ordinal),
        "HTML report should display the manual adjustment audit");
    Assert(
        File.ReadAllText(exports.Html).Contains("2.100, 1.900", StringComparison.Ordinal),
        "HTML report should display complete qualifying lap history");
}
finally
{
    if (Directory.Exists(exportDirectory))
    {
        Directory.Delete(exportDirectory, recursive: true);
    }
}

string databaseTestDirectory = Path.Combine(
    Path.GetTempPath(),
    "YATSS.Tests",
    Guid.NewGuid().ToString("N"));
string testDatabasePath = Path.Combine(databaseTestDirectory, "active.db");
string testBackupPath = Path.Combine(databaseTestDirectory, "manual-backup.db");
string testSafetyPath = Path.Combine(databaseTestDirectory, "before-restore.db");
string testAutomaticDirectory = Path.Combine(databaseTestDirectory, "Automatic");
try
{
    Directory.CreateDirectory(databaseTestDirectory);
    DatabaseMaintenance maintenance = new(
        testDatabasePath,
        testAutomaticDirectory,
        currentSchemaVersion: 1);

    CreateTestDatabase(testDatabasePath, schemaVersion: 0, "Pre-Migration Racer");
    maintenance.BackUpBeforeSchemaUpgrade();
    string schemaBackupPath = Directory.GetFiles(
        testAutomaticDirectory,
        "YATSS-before-schema-v0-to-v1-*.db").Single();
    Assert(ReadTestRacers(schemaBackupPath).SequenceEqual(new[] { "Pre-Migration Racer" }),
        "schema upgrade should preserve a verified copy of the old database");

    CreateTestDatabase(testDatabasePath, schemaVersion: 1, "Current Racer");

    DatabaseBackupResult manualBackup = maintenance.CreateBackup(testBackupPath);
    Assert(manualBackup.RacerCount == 1, "manual backup should report its racer count");
    Assert(ReadTestRacers(testBackupPath).SequenceEqual(new[] { "Current Racer" }),
        "manual backup should contain current data");

    CreateTestDatabase(testDatabasePath, schemaVersion: 1, "Changed Racer");
    DatabaseRestoreResult restore = maintenance.RestoreBackup(
        testBackupPath,
        testSafetyPath,
        closeActiveDatabase: () => { },
        initializeActiveDatabase: () => { });
    Assert(restore.RacerCount == 1, "restore should report its racer count");
    Assert(ReadTestRacers(testDatabasePath).SequenceEqual(new[] { "Current Racer" }),
        "restore should replace the active database");
    Assert(ReadTestRacers(testSafetyPath).SequenceEqual(new[] { "Changed Racer" }),
        "restore should preserve the previous database in a safety backup");

    Directory.CreateDirectory(testAutomaticDirectory);
    File.Copy(testBackupPath, Path.Combine(testAutomaticDirectory, "YATSS-auto-20000101.db"));
    File.Copy(testBackupPath, Path.Combine(testAutomaticDirectory, "YATSS-auto-20000102.db"));
    DatabaseBackupResult? automaticBackup = maintenance.CreateAutomaticBackup(retainedBackupCount: 2);
    Assert(automaticBackup is not null, "first daily automatic backup should be created");
    Assert(Directory.GetFiles(testAutomaticDirectory, "YATSS-auto-*.db").Length == 2,
        "automatic backup retention should remove older daily copies");
    Assert(maintenance.CreateAutomaticBackup(retainedBackupCount: 2) is null,
        "only one automatic backup should be created per day");

    string legacyBackupPath = Path.Combine(databaseTestDirectory, "legacy-backup.db");
    CreateTestDatabase(legacyBackupPath, schemaVersion: 0, "Legacy Racer");
    CreateTestDatabase(testDatabasePath, schemaVersion: 1, "Before Legacy Restore");
    _ = maintenance.RestoreBackup(
        legacyBackupPath,
        testSafetyPath,
        closeActiveDatabase: () => { },
        initializeActiveDatabase: () => SetTestSchemaVersion(testDatabasePath, 1));
    Assert(ReadTestRacers(testDatabasePath).SequenceEqual(new[] { "Legacy Racer" }),
        "restore should allow an older YATSS schema and migrate it");

    CreateTestDatabase(testDatabasePath, schemaVersion: 1, "Rollback Racer");
    int initializationAttempts = 0;
    bool restoreFailed = false;
    try
    {
        _ = maintenance.RestoreBackup(
            testBackupPath,
            testSafetyPath,
            closeActiveDatabase: () => { },
            initializeActiveDatabase: () =>
            {
                initializationAttempts++;
                if (initializationAttempts == 1)
                {
                    throw new InvalidOperationException("Simulated initialization failure");
                }
            });
    }
    catch (InvalidOperationException)
    {
        restoreFailed = true;
    }

    Assert(restoreFailed, "a failed restore should report failure");
    Assert(ReadTestRacers(testDatabasePath).SequenceEqual(new[] { "Rollback Racer" }),
        "a failed restore should roll back to the previous database");

    string newerBackupPath = Path.Combine(databaseTestDirectory, "newer-backup.db");
    CreateTestDatabase(newerBackupPath, schemaVersion: 2, "Future Racer");
    bool newerBackupRejected = false;
    try
    {
        _ = maintenance.RestoreBackup(
            newerBackupPath,
            testSafetyPath,
            closeActiveDatabase: () => { },
            initializeActiveDatabase: () => { });
    }
    catch (InvalidDataException)
    {
        newerBackupRejected = true;
    }
    Assert(newerBackupRejected, "restore should reject a database from a newer schema");
}
finally
{
    if (Directory.Exists(databaseTestDirectory))
    {
        Directory.Delete(databaseTestDirectory, recursive: true);
    }
}

QualifyingController qualifying = new();
qualifying.Configure(new[] { "Slow", "No Lap", "Fast" }, laneIndex: 2, durationSeconds: 30);
Assert(qualifying.State == QualifyingState.Ready, "qualifying should be ready after configuration");
Assert(qualifying.CurrentRacer == "Slow", "qualifying should preserve initial racer order");
Assert(qualifying.Start(1000), "first qualifier should start");
Assert(!qualifying.IsExpired(30999), "qualifier should not expire early");
Assert(qualifying.IsExpired(31000), "qualifier should expire at configured duration");
Assert(qualifying.InterruptCurrent(), "running qualifier should allow a watchdog interruption");
Assert(qualifying.State == QualifyingState.Ready, "interrupted qualifier should be ready to rerun");
Assert(qualifying.CurrentRacer == "Slow", "interrupted qualifier should not advance the racer");
Assert(qualifying.Start(1000), "interrupted qualifier should restart");
Assert(qualifying.CompleteCurrent(
    new[]
    {
        new LaneLapRecord(2700, true, 5000),
        new LaneLapRecord(2500, true, 7500)
    },
    31000), "first qualifier should complete with lap history");
Assert(qualifying.CurrentRacer == "No Lap", "qualifying should advance to next racer");
Assert(qualifying.Start(40000), "second qualifier should start");
Assert(qualifying.CompleteCurrent(null), "no-lap qualifier should complete");
Assert(qualifying.Start(80000), "third qualifier should start");
Assert(qualifying.CompleteCurrent(2100), "third qualifier should complete");
Assert(qualifying.State == QualifyingState.Complete, "qualifying should complete after every racer");
IReadOnlyList<QualifyingResult> rankedQualifiers = qualifying.GetRankedResults();
Assert(rankedQualifiers[0].RacerName == "Fast", "fastest qualifier should rank first");
Assert(rankedQualifiers[1].RacerName == "Slow", "slower valid qualifier should rank next");
Assert(rankedQualifiers[2].RacerName == "No Lap", "qualifier without a lap should rank last");
QualifyingResult slowQualifier = rankedQualifiers.Single(result => result.RacerName == "Slow");
Assert(slowQualifier.LaneIndex == 2, "qualifying result should retain its lane");
Assert(slowQualifier.ConfiguredDurationSeconds == 30, "qualifying result should retain configured duration");
Assert(slowQualifier.ElapsedMilliseconds == 30000, "qualifying result should retain actual session duration");
Assert(slowQualifier.Laps.Count == 2, "qualifying result should retain every accepted lap");
Assert(slowQualifier.Laps[1].SessionElapsedMilliseconds == 6500, "qualifying lap should retain session elapsed time");
Assert(slowQualifier.BestLapMilliseconds == 2500, "qualifying best lap should be derived from retained laps");

Assert(
    BuildIdentity.Normalize("v0.10.0-beta.1-0-gc2fb82c", "0.10.0-beta.1") == "v0.10.0-beta.1",
    "an exact clean release tag should display without commit metadata");
Assert(
    BuildIdentity.Normalize("v0.10.0-beta.1-3-g1a2b3c4", "0.10.0-beta.1") == "v0.10.0-beta.1-3-g1a2b3c4",
    "an intermediate clean build should display commit distance and hash");
Assert(
    BuildIdentity.Normalize("v0.10.0-beta.1-0-gc2fb82c-dirty", "0.10.0-beta.1") ==
        "v0.10.0-beta.1-0-gc2fb82c-dirty",
    "an exact-tag dirty build should retain its hash and dirty marker");
Assert(
    BuildIdentity.Normalize("1a2b3c4-dirty", "0.10.0-beta.1") == "git-1a2b3c4-dirty",
    "an untagged dirty build should be identified as a Git build");
Assert(
    BuildIdentity.Normalize(null, "0.10.0-beta.1+metadata") == "v0.10.0-beta.1",
    "a source archive build should fall back to the project version");

QualifyingController trackCallQualifying = new();
trackCallQualifying.Configure(new[] { "Track Call" }, laneIndex: 0, durationSeconds: 30);
Assert(trackCallQualifying.Start(1000), "track-call qualifier should start");
LapEdge beforeTrackCall = trackCallQualifying.AdjustEdgeTimestamp(new LapEdge(0, 1, 6000));
Assert(beforeTrackCall.TimestampMillis == 6000, "qualifying timestamp should initially follow controller time");
Assert(trackCallQualifying.Pause(11000), "running qualifying should pause for a track call");
Assert(trackCallQualifying.State == QualifyingState.Paused, "qualifying track call should enter paused state");
Assert(trackCallQualifying.GetRemaining(21000) == TimeSpan.FromSeconds(20), "qualifying timer should freeze during a track call");
Assert(!trackCallQualifying.IsExpired(41000), "paused qualifying should not expire");
Assert(trackCallQualifying.Resume(21000), "paused qualifying should resume");
LapEdge afterTrackCall = trackCallQualifying.AdjustEdgeTimestamp(new LapEdge(0, 2, 26000));
Assert(afterTrackCall.TimestampMillis == 16000, "qualifying lap timing should exclude stopped time");
Assert(!trackCallQualifying.IsExpired(40999), "resumed qualifying should not expire early");
Assert(trackCallQualifying.IsExpired(41000), "resumed qualifying should expire after active time only");
Assert(trackCallQualifying.CompleteCurrent(
    new[]
    {
        new LaneLapRecord(5000, true, beforeTrackCall.TimestampMillis),
        new LaneLapRecord(10000, true, afterTrackCall.TimestampMillis)
    },
    41000), "track-call qualifier should complete");
QualifyingResult trackCallResult = trackCallQualifying.GetRankedResults().Single();
Assert(trackCallResult.ElapsedMilliseconds == 30000, "qualifying result should exclude track-call time");
Assert(trackCallResult.Laps[1].SessionElapsedMilliseconds == 15000, "qualifying report timing should exclude track-call time");

IReadOnlyList<string> seededQualifiers = QualifyingController.BuildSeededRacers(
    new[]
    {
        new QualifyingResult("A", 0, 1000),
        new QualifyingResult("B", 1, 1100),
        new QualifyingResult("C", 2, 1200),
        new QualifyingResult("D", 3, 1300),
        new QualifyingResult("E", 4, 1400)
    },
    new[] { 2, 0, 3, 1 },
    activeLaneCount: 4);
Assert(seededQualifiers.SequenceEqual(new[] { "B", "D", "A", "C", "E" }), "lane choices should seed physical lanes and preserve qualifying queue order");

Console.WriteLine("Protocol, lap race, export, and database tests passed.");

static void CreateTestDatabase(string path, int schemaVersion, params string[] racers)
{
    File.Delete(path);
    File.Delete(path + "-shm");
    File.Delete(path + "-wal");
    using SqliteConnection connection = new(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Pooling = false
    }.ToString());
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE users (name TEXT NOT NULL);";
    command.ExecuteNonQuery();
    foreach (string racer in racers)
    {
        command.CommandText = "INSERT INTO users (name) VALUES ($name);";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$name", racer);
        command.ExecuteNonQuery();
    }
    command.CommandText = $"PRAGMA user_version = {schemaVersion};";
    command.Parameters.Clear();
    command.ExecuteNonQuery();
}

static IReadOnlyList<string> ReadTestRacers(string path)
{
    using SqliteConnection connection = new(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString());
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "SELECT name FROM users ORDER BY name;";
    using SqliteDataReader reader = command.ExecuteReader();
    List<string> racers = new();
    while (reader.Read())
    {
        racers.Add(reader.GetString(0));
    }
    return racers;
}

static void SetTestSchemaVersion(string path, int schemaVersion)
{
    using SqliteConnection connection = new(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Pooling = false
    }.ToString());
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = $"PRAGMA user_version = {schemaVersion};";
    command.ExecuteNonQuery();
}
