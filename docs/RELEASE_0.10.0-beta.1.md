# YATSS 0.10 Beta 1

YATSS 0.10 Beta 1 is the first public prerelease of the YATSS slot-car timing
and scoring system. It is suitable for evaluation, demo races, controller
development, and careful bench testing. It has not yet operated a production
track installation.

## Highlights

- Eight-lane timing and track-power control with ESP32-C6-DevKitC-1 and Arduino
  Nano ESP32 pin profiles.
- Practice timing, qualifying, multi-heat races, racer rotation, track calls,
  intermissions, manual lap corrections, and races up to 24 hours per heat.
- HTML race reports plus optional versioned JSON and normalized CSV exports.
- Verified database backup and restore with daily automatic backups.
- Controller diagnostics for live sensor states, accepted edges, relay pulse
  tests, uptime, debounce, and queue-overflow counts.
- Optional voice announcements and a silent countdown path.
- Space or the Next button on an unmodified Logitech R500s controls race starts,
  track calls, resumes, and intermission pausing.
- Self-contained Windows x64 distribution; a separate .NET installation is not
  required.

## Tested Environments

- Windows x64 is the primary supported environment.
- Wine on x64 Linux is experimental. The application and controller serial
  heartbeats have been verified when the Linux serial device is mapped to a
  Wine COM port.
- Wine on ARM64 Linux is experimental. The ARM64 build, redirected UI, and
  controller heartbeats have been exercised on a Rock5B.

The ESP32-C6 profile compiles, uploads, and communicates over the board's UART
connector. Complete eight-lane sensor, relay, watchdog, and production harness
validation remains outstanding.

## Hardware Safety

YATSS cuts all lanes when controller communication is lost for five seconds,
provided that the controller and relay-coil supply remain powered. The current
normally closed relay arrangement cannot remain power-off if controller or
relay-coil power itself is lost. Bench-test the complete installation and use a
normally open safety contactor or independent hardwired interlock where loss of
control power must fail to track power off.

## Installation

1. Choose a package:
   - `YATSS-win-x64-v0.10.0-beta.1.zip` is the recommended self-contained x64
     package and does not require a separately installed .NET runtime.
   - `YATSS-win-x64-requires-dotnet10-v0.10.0-beta.1.zip` is a smaller x64
     package for systems with the x64 .NET 10 Desktop Runtime installed.
   - `YATSS-win-arm64-v0.10.0-beta.1-experimental.zip` is the experimental,
     self-contained Windows ARM64 package.
2. Verify its SHA-256 value against the attached checksum file.
3. Extract the complete ZIP to a writable folder.
4. Run `YATSSWin.exe`.
5. Use `Mode > Demo Race...`; it starts Simulated Lap Input automatically so the
   workflow can be evaluated without controller hardware.

The executable is not code-signed, so Windows may display a reputation warning
for this first prerelease. Verify that the ZIP came from the official GitHub
release and that its checksum matches before running it.

Configuration, logs, databases, backups, and race reports are written to the
locations documented in the repository README and Windows-app guide; they are
not stored in the extracted application directory.

## Known Limitations

- There are no production installations yet.
- Crash recovery for an active race is not implemented.
- Database restore validates SQLite integrity, foreign keys, schema version,
  and core tables, but complete required-column validation remains planned.
- Full ESP32-C6 eight-lane hardware and relay validation remains planned.
- The binaries are not code-signed.

See [TODO.md](../TODO.md) for the current engineering backlog and
[PUBLISH_SMOKE_TEST.md](PUBLISH_SMOKE_TEST.md) for the release validation
runbook.
