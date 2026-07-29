# YATSS Windows App

This is the Windows WinForms side of the slot-car lap timer. The app owns race
state, lap counting, filtering, logging, heat-race flow, qualifying, reports,
and track-power commands. The microcontroller only reports timestamped sensor
edges.

The Carolina Blue title panel at the bottom of the main window displays the
application release version in its lower-right corner. The value comes from
version metadata embedded at build time.

Builds made from an exact clean release tag show the concise tag, such as
`v0.10.0-beta.1`. Intermediate builds use Git's description, such as
`v0.10.0-beta.1-3-g1a2b3c4`; uncommitted source changes append `-dirty`. The
same identity appears in the window title. Builds made from a source archive
without Git metadata fall back to the project version.

## Build And Test

From the `YATSSWin` directory:

```powershell
dotnet build YATSS.sln -c Release
dotnet run --project YATSS.Tests\YATSS.Tests.csproj -c Release
```

From the repository root:

```powershell
dotnet build YATSSWin\YATSS.sln -c Release
dotnet run --project YATSSWin\YATSS.Tests\YATSS.Tests.csproj -c Release
```

The app targets `.NET 10` LTS for Windows Forms.

## Serial Connection

Configure the COM port from the app's Configure dialog. The selected port is
persisted locally. Serial traffic is logged under:

```text
%LOCALAPPDATA%\YATSS\logs\serial-YYYYMMDD.log
```

App settings, lane configuration, and racer names are stored in:

```text
%LOCALAPPDATA%\YATSS\laps.db
```

The `Data` menu creates verified manual backups, restores verified backups with
a pre-restore safety copy, and opens the database or backup folders. YATSS also
creates one automatic backup per day and retains the newest 14 under:

```text
%USERPROFILE%\Documents\YATSS Backups\Automatic
```

Successful restore cuts track power and restarts YATSS so every restored
setting is loaded. See [Database backup and restore](../docs/DATABASE_BACKUP.md)
for validation, retention, schema-upgrade, and rollback details.

The `File > Serial Log` window tails the current log. It follows the end of the
file while scrolled to the bottom, pauses when you scroll up to inspect older
lines, and resumes following when you scroll back to the bottom.

While YATSS is running, the app asks Windows to keep the system and display
awake so the race board does not sleep or blank during timing. Normal power
management resumes when the app exits.

## Controller Diagnostics

`File > Controller Diagnostics` opens a live eight-lane wiring view when the
controller is connected and YATSS is idle in Practice mode. It displays each
raw sensor state, transition count, debounced accepted-edge count, track-power
state, controller uptime, debounce setting, and cumulative queue-overflow count.

Each lane has a controller-timed `Pulse Cut` relay test. Pulses only remove
power and automatically restore the previous mask. The window can also clear
transition counts, refresh controller status, or cut all track power. Diagnostic
sensor activity is logged but never enters the lap-counting path. Starting
another race mode closes diagnostics first.

## Controller Firmware Updates

`File > Update Controller Firmware...` installs the bundled YATSSMC image on
an ESP32-C6-DevKitC-1 from Practice mode. It works with an identified running
controller or a blank C6 when the correct COM port is selected. Track power and
relay-coil power must be physically disconnected during flashing.

YATSS reuses `esptool.exe` from an installed Espressif Arduino core or from
`YATSS_ESPTOOL_PATH`. If neither exists, the operator can approve a direct
download of Espressif's official pinned release; YATSS verifies the archive's
published SHA-256 and caches it under `%LOCALAPPDATA%\YATSS\Tools`. The
uploader is not bundled in YATSS release ZIPs. See
[Controller firmware updates](../docs/CONTROLLER_FIRMWARE_UPDATE.md).

## Practice Mode

Practice is the default mode. Edges from active lanes establish a baseline on
the first pass, then subsequent valid edges count laps. The app filters laps
using:

- Minimum lap time
- Windows raw edge lockout
- Controller-side debounce
- Active lane count

The first edge on a lane establishes the baseline. The board shows `0` laps
after the baseline and keeps timing fields blank until a real counted lap is
available.

## Heat Race Mode

Heat race setup supports:

- Race name
- Heat length from 1 minute through 24 hours
- Time between heats
- Active lane count from 2 through 8
- More racers than lanes
- Lane names and colors
- Optional qualifying

Space starts a heat, pauses a running heat for a track call, and resumes a
paused heat. Track power is cut immediately on track calls and restored after
the three-second countdown. Voice announcements are enabled by default and can
be disabled in Configure; the same countdown runs silently when they are off.
The main status band shows a gold Space-bar prompt before the first heat and
while paused, then changes to a countdown state after Space is pressed. During
stoppage time, lap counts can be adjusted by lane.

Space also makes and resumes track calls during qualifying. Qualifying time and
any lap spanning the track call exclude the stopped interval. During a timed
intermission, the first Space press pauses the automatic next-heat start; the
status band then prompts for a subsequent Space press to start the next heat.

The Next button on an unmodified Logitech R500s presenter sends Right Arrow;
YATSS accepts that button as the same race-control command as Space. The
presenter's Back and laser buttons have no race-control function.

After a race, the app always writes a human-readable HTML report under:

```text
%USERPROFILE%\Documents\YATSS Race Reports
```

The `Race Reports` section in Configure independently enables a versioned JSON
archive and normalized CSV exports. Both optional exports default to enabled.
The JSON archive preserves race settings, lane metadata, qualifying sessions,
accepted heat laps, results, standings, and manual lap adjustments. Numeric lap
times remain in milliseconds so another program can format them without losing
precision. CSV exports are written alongside each archive:

- `_results.csv` contains one row per racer/lane/heat result.
- `_laps.csv` contains one row per accepted heat lap.
- `_qualifying.csv` contains every accepted qualifying lap and session details.
- `_adjustments.csv` contains the manual lap-correction audit trail.

All files for a race share the same timestamped `HeatRace_yyyyMMdd_HHmmss`
basename. Raw and rejected controller traffic remains in the serial log rather
than the race archive.

See [Race reports and data exports](../docs/RACE_DATA_EXPORT.md) for the full
schema, CSV column definitions, timing semantics, and schema lifecycle policy.

## Demo Mode

The Mode menu includes demo options for exercising the app without relying on
live sensor hardware.

`Demo Race` seeds a heat race with sample racers so the heat-race workflow can
be tested quickly.

`Simulated Lap Input` generates simulated controller heartbeats and lane edges. It
uses the same lap-processing path as real serial input, but ignores real serial
lines while the demo stream is active. This is useful for testing timing board
updates, heat transitions, qualifying and race setup behavior, and report
output.

In the Mode menu, Practice and Heat Race use mutually exclusive radio marks.
Qualifying and Demo Race are setup commands and do not remain marked. Simulated
Lap Input is a separate on/off toggle, so its check can appear alongside either
primary mode while simulated edges are active.

## Configure Dialog

Current persisted settings include:

- Serial port
- Minimum lap time
- Track length in feet
- Active lane count
- Lane names and colors
- Optional SAPI voice announcements and selected voice; when announcements are
  disabled, race starts retain a silent three-second countdown without loading SAPI
- Sound on too-fast laps
- Controller sensor debounce
- Windows raw edge lockout

Changing controller sensor debounce sends `CONFIG:DEBOUNCE:<milliseconds>` to
the controller when connected.

## Track Power

The app sends track-power commands over serial. In practice mode it enables the
configured active lanes. In heat-race mode it enables only occupied lanes and
cuts power during intermissions and track calls.

Windows acknowledges each controller heartbeat. If acknowledgements stop for
five seconds while a lane is powered, the controller cuts every lane. A
watchdog report pauses a running heat or returns the current qualifier to Ready
for another attempt; routine communication resumption does not itself restore
track power.

See `..\docs\SERIAL_PROTOCOL.md` for the serial protocol and
`..\docs\TROUBLESHOOTING.md` for Visual Studio and upload recovery notes.
