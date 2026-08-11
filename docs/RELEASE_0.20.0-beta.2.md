# YATSS 0.20 Beta 2

YATSS 0.20 Beta 2 expands the public beta with integrated controller firmware
installation, Arduino UNO Q support, improved Wine operation, and more complete
race workflow and reporting. It is suitable for evaluation, demo races,
controller development, and careful bench testing. YATSS still has no
production track installations.

## Highlights

- In-app firmware installation for ESP32-C6 N4/N8, Waveshare ESP32-C5 N16R8,
  and Arduino Nano ESP32 controllers. Board identity and flash capacity are
  checked before writing.
- Experimental Arduino UNO Q controller application using the STM32U585 for
  interrupt-driven sensor timing and localhost TCP communication with YATSS.
- Piper speech on Windows and through the native Linux helper under Wine, in
  addition to Windows SAPI and eSpeak NG.
- Visual start lights synchronized with the race countdown, track power applied
  immediately before "Let's go," and 60, 30, and 15-second announcements for
  longer intermissions.
- More reliable serial and TCP reconnection, including Wine-specific serial
  handling and quieter expected read timeouts.
- Completed-race HTML report display, qualifying details, optional JSON/CSV
  exports, database backup/restore, and automatic daily backups.
- Clear Space-bar prompts for starts and track-call resumes, pausable timed
  intermissions, and qualifying track calls.
- Build identity in the title panel and an updated public overview with demo
  race, practice, idle, and report screenshots.

## Tested Environments

- Windows x64 is the primary supported environment.
- Wine on x64 Linux is experimental. The app, mapped serial controller,
  reports, firmware updates, and native Linux speech helper have been tested.
- Wine on ARM64 Linux is experimental. The ARM64 app, native X11 UI, mapped
  serial controller, reports, and controller updates have been exercised.
- Wine on Arduino UNO Q is experimental. The App Lab STM32 controller and
  localhost TCP transport passed an overnight demo-lap soak test, and lane 1
  sensor input was validated end to end.

## Hardware Safety

YATSS cuts all lanes when controller communication is lost for five seconds,
provided that the controller and relay-coil supply remain powered. The current
normally closed relay arrangement cannot remain power-off if controller or
relay-coil power itself is lost. Bench-test the complete installation and use a
normally open safety contactor or independent hardwired interlock where loss of
control power must fail to track power off.

## Installation

1. Choose a package:
   - `YATSS-win-x64-v0.20.0-beta.2.zip` is the recommended self-contained x64
     package and does not require a separately installed .NET runtime.
   - `YATSS-win-x64-requires-dotnet10-v0.20.0-beta.2.zip` is a smaller x64
     package for systems with the x64 .NET 10 Desktop Runtime installed.
   - `YATSS-win-arm64-v0.20.0-beta.2-experimental.zip` is the experimental,
     self-contained Windows ARM64 package used with ARM64 Windows or Wine.
   - `YATSS-UNOQ-AppLab-v0.20.0-beta.2.zip` is the experimental UNO Q
     controller application for import into Arduino App Lab.
2. Verify the download against `YATSS-v0.20.0-beta.2-SHA256.txt`.
3. Extract the complete ZIP to a writable folder.
4. Run `YATSSWin.exe`.
5. Select `Mode > Demo Race...` to exercise the race workflow without hardware.

The Windows executable is not code-signed, so Windows may display a reputation
warning. Verify that the ZIP came from the official GitHub release and that its
checksum matches before running it.

The Windows packages include matching controller firmware. Official uploader
utilities are not redistributed; YATSS reuses an installed tool or downloads a
pinned official copy after operator approval and checksum verification.

## Known Limitations

- There are no production installations yet.
- Crash recovery for an active race is not implemented.
- Database restore validates SQLite integrity, foreign keys, schema version,
  and core tables, but complete required-column validation remains planned.
- Full eight-lane sensor, relay, watchdog, and production-harness validation is
  still pending for the C5, C6, and UNO Q controller profiles.
- UNO Q automatic production startup is not yet implemented; Arduino App Lab
  must keep the controller application running.
- Wine environments and the ARM64 package remain experimental.
- The binaries are not code-signed.

See [TODO.md](../TODO.md) for the current engineering backlog and
[PUBLISH_SMOKE_TEST.md](PUBLISH_SMOKE_TEST.md) for the release validation
runbook.
