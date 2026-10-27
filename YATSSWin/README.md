# YATSS Windows App

This is the Windows WinForms side of the slot-car lap timer. The app owns race
state, lap counting, filtering, logging, heat-race flow, qualifying, reports,
and track-power commands. The microcontroller only reports timestamped sensor
edges.

The Carolina Blue title panel at the bottom of the main window displays the
application release version in its lower-right corner. The value comes from
version metadata embedded at build time.

Builds made from an exact clean release tag show the concise tag, such as
`v0.20.0-beta.2`. Intermediate builds use Git's description, such as
`v0.20.0-beta.2-3-g1a2b3c4`; uncommitted source changes append `-dirty`. The
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
%LOCALAPPDATA%\YATSS\YATSS.db
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

Logging runs in a bounded background queue so a slow or failed disk does not
block lap counting. Files roll over at midnight and retain the last 30 calendar
days. Storage errors are reported, and the log window shows dropped diagnostic
entries. See [Serial logging](../docs/SERIAL_LOGGING.md) for retry and shutdown
limits; these logs are not a durable race-recovery journal.

While YATSS is running, the app asks Windows to keep the system and display
awake so the race board does not sleep or blank during timing. Normal power
management resumes when the app exits.

## Help and Installation

The optional native Windows installer includes the self-contained app and
bundled assets, adds per-user shortcuts and preserves race data on uninstall.
No separate .NET install is needed. Continue using portable ZIPs under Wine.
See [Windows installer](../docs/WINDOWS_INSTALLER.md) for build commands, update
behavior, safety checks and current validation limits.

Self-contained Windows/Wine regression ZIPs can be built with
`tools/Build-ValidationPackage.ps1` from the repository root. Packaged launchers
run isolated test areas and retain summaries, structured results and logs.
See [Software validation](../docs/SOFTWARE_VALIDATION.md) for Wine prefix,
architecture, startup and failure-reporting requirements. This is not a real
hardware test or complete director UI acceptance run.

The Help menu opens locally packaged guides as selectable plain text; Markdown
notation remains visible. Internet is not required to read them.
Each guide has Copy Selection, Copy All and Open
Original controls. Copy Selection is enabled when text is selected and preserves
that selection when the toolbar takes focus. Copy All copies the complete plain
text, including commands. Open Original opens the packaged Markdown file using
the system's file association; an associated editor/viewer must be installed.
Clipboard or open failures show a warning and leave the guide open. Missing guides
offer an online fallback. The menu includes Quick Start and Demo, race operation,
Combined Distance, controller boards/pin maps, track-power wiring, an installer
commissioning checklist, troubleshooting, reruns and report exports.

Copy Version Information includes the app build, runtime, architecture, OS,
simulated-input state and last identified controller firmware. Controller
identity is not a live readiness guarantee. No racer names, private paths or
connection addresses are included. Report a Bug opens GitHub; review any logs
or screenshots for private data before attaching them. About YATSS includes
version information, the MIT license and development/verification status.

The commissioning worksheet complements, rather than replaces, the longer
developer publish smoke test. It records sensor/power checks and physical race
verification. Neither a completed worksheet nor a demo is safety certification.

## Independent Heat Groups

Mode > Heat Race offers **Scheduling: Rotation Queue / Independent Groups**.
The default queue remains available. Independent Groups requires fastest-lap
qualifying, then separate lane choices for consecutive qualifying-ranked groups.
Each group completes a full rotation before the next starts; group changes
always wait for Space. Ten racers on eight lanes form 8+2 and run sixteen heats.
Final results compare racing laps only; qualifying contributes no lap credit.
Settings, recovery and exports retain the scheduling/group assignments. See
[Independent heat groups](../docs/INDEPENDENT_HEAT_GROUPS.md) for counting and ties.

## Race Archives

**Data > Race Archives...** browses saved events while stopped with power off.
Inspect unfinished snapshots, reopen original reports, or regenerate completed
reports into unique export-copy folders. Confirmed deletion supports individual
archives or archives older than a selected date; active events and exported
files are never deleted. No automatic retention deletion is enabled. See
[Race archives and retention](../docs/RACE_ARCHIVES.md) for guarantees and limits.

## Active Event Recovery

Unfinished races and qualifying sessions are saved separately in
`%LOCALAPPDATA%\YATSS\ActiveRace.db`. Startup offers Resume Event, Archive and
Discard, or Close YATSS. Recovery holds power off until the director reviews
totals/positions and explicitly resumes. Interrupted qualifiers are rerun;
earlier completed results remain. Ordinary practice is not journaled.

Settings backups do not include this journal. See
[Active event recovery](../docs/ACTIVE_RACE_RECOVERY.md) for storage failures,
outage-crossing uncertainty, pending approvals, and first-version limits.

**Mode > Rerun Current Heat...** discards a stopped current heat attempt,
restores its starting totals and full duration, and retains an audit. Earlier
heats, qualifying credit and lane assignments stay intact. Power stays off;
restore cars to their heat-start positions and press Space explicitly. See
[Current-heat reruns](../docs/HEAT_RERUNS.md) for eligibility and export limits.

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
a Waveshare ESP32-C5-WIFI6-KIT-N16R8, ESP32-C6-DevKitC-1 N4/N8, or Arduino
Nano ESP32 from Practice mode.
Protocol-v4 controllers select their board and flash-capacity package
automatically. Unidentified C5/C6 hardware is read-only probed after the
operator confirms the printed board family; this verifies the C5 chip and
capacity or selects C6 N4 versus N8. The chip and capacity are probed again
immediately before writing. Track power and relay-coil power must be physically
disconnected during flashing.

YATSS reuses `esptool.exe` from an installed Espressif Arduino core or from
`YATSS_ESPTOOL_PATH`. If neither exists, the operator can approve a direct
download of Espressif's official pinned release; YATSS verifies the archive's
published SHA-256 and caches it under `%LOCALAPPDATA%\YATSS\Tools`. The
uploader is not bundled in YATSS release ZIPs. Nano updates similarly reuse
Arduino's installed `dfu-util.exe` or download Arduino's pinned official build
after approval and SHA-256 verification. See
[Controller firmware updates](../docs/CONTROLLER_FIRMWARE_UPDATE.md).

UNO Q updates use the same menu command when YATSS runs locally under Wine on
the UNO Q. Install the bundled authenticated Linux helper first; see
[UNO Q setup and recovery](../docs/UNO_Q_UPDATES.md). Initial provisioning still
uses App Lab. Remote Windows-to-UNO Q flashing is not supported in this version.

Physical timing requires session-aware protocol-5 firmware on all boards. The
app detects a new controller timing session even when the clock did not roll
back, rejects retired-session messages, and leaves racing paused until the
director explicitly resumes after readiness verification. Update the app and
controller firmware together; see [recovery](../docs/CONTROLLER_RECOVERY.md).

## Practice Mode

After a controller identifies itself, development builds offer a newer matching
bundled firmware version when Practice is safe for updating and track power is
off. No is default; Yes opens the normal updater and its safety confirmation.
The comparison works offline and does not repeat dismissed offers on reconnect
within the same app session. See [firmware updates](../docs/CONTROLLER_FIRMWARE_UPDATE.md).

Development source supports **Mode > Practice Sensor Lanes...** with matching
`0.25.0-capture.1` firmware. It arms selected controller inputs without changing
relay power. Selection defaults to all configured lanes on app start and lasts
for the app session. Rearming preserves totals/bests but starts fresh timing.
See [Practice sensor arming](../docs/PRACTICE_SENSOR_ARMING.md).

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

Lap-best sounds are enabled by default and are independent of voice
announcements and the ignored-lap warning. A lane's first eligible timed lap
establishes its lane benchmark silently. A later strict lane improvement plays
one pip. During a running heat, the first eligible timed lap establishes the
overall heat best with two pips; every later lap that beats it also plays the
two-pip signal instead of the lane signal. Heat benchmarks reset when racers
rotate, Practice benchmarks remain until Practice or the lane is reset, and
qualifying benchmarks reset for each racer. Closely spaced signals are
coalesced rather than queued; a heat-best signal takes priority.

## Lap Finish Mode

In Heat Race setup, select **Lap Finish** and enter **Target laps**.
Enable `Mode > Simulated Lap Input` first to try it without hardware.
Or choose `Mode > Lap Finish Demo...` for a preset 10-lap race with one demo
racer per active lane and simulated input enabled automatically.
Two to eight racers (no more than active lanes) race on fixed lanes; the first
accepted target-lap crossing stops everyone. The first sensor crossing starts
each lane's timing without counting a lap. Space track calls, paused corrections
below the target, recovery, reruns and reports are supported. There is no time
limit, qualifying or rotation in this first version. See
[Lap Finish racing](../docs/LAP_FINISH.md) for finish ordering and full operation.
Available in development source after `v0.25.0-beta.3`.

## Heat Race Mode

Heat race setup supports:

- Race name
- Heat length from 1 second through 24 hours
- Time between heats
- Active lane count from 2 through 8
- More racers than lanes
- Lane names and colors
- Optional qualifying

Space starts a heat, pauses a running heat for a track call, and resumes a
paused heat. Track power is cut immediately on track calls and restored after
the synchronized start countdown. Track power and race timing start immediately
before the separate "Let's go" announcement, so the audible start agrees with
the physical start. Voice announcements are enabled by default
and can be disabled in Configure; the same 1.5-second minimum countdown and
visual start lights operate silently when speech is off or unavailable.
Intermissions of at least one minute announce 60, 30, and 15 seconds remaining
when voice announcements are enabled.
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

Heat Race setup uses whole seconds for heat length, just like intermissions:
30 is half a minute, 90 is a minute and a half, and 180 is three minutes.
Heat lengths range from 1 to 86,400 seconds (24 hours), with a default of 180.
Intermission defaults to 60 seconds when no setup has been saved. Previously
saved values, including zero, are retained for both race formats.
Combined Distance uses the same field for each racing segment. Demo Race still
uses 60-second heats. Stopped time does not consume the configured duration.
Saved minute-based settings and unfinished events are converted automatically
without changing their duration. HTML, JSON, and results CSV record heat seconds.

After a race, the app always writes a human-readable HTML report under:

```text
%USERPROFILE%\Documents\YATSS Race Reports
```

The `Race Reports` section in Configure independently enables a versioned JSON
archive and normalized CSV exports. Both optional exports default to enabled.
Event-specific filenames and saved export choices let interrupted exports retry
without duplicate report copies. Files are staged before atomic replacement;
a `.yatss-export` completion receipt is written last. If an export fails, fix
storage/access, restart, and Finish Exports to finish the completed race's exports
with power off. See [Race Reports and Data Exports](../docs/RACE_DATA_EXPORT.md).
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

`Simulated Lap Input` generates simulated controller heartbeats and lane edges.
It uses the same lap-processing path as real serial input and ignores physical
sensor edges for scoring while the demo stream is active. Physical controller
heartbeats are still acknowledged so its safety watchdog remains healthy. This
is useful for testing timing board updates, heat transitions, qualifying and
race setup behavior, and report output.

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
- Optional voice announcements, speech engine, and selected voice. Automatic
  mode prefers Windows SAPI when it has voices, then Piper, then the native
  eSpeak NG helper. Disabled or unavailable speech retains the visual
  1.5-second minimum countdown.
- Sound on too-fast laps
- Lane-best and overall heat-best lap sounds
- Controller sensor debounce
- Windows raw edge lockout

Changing controller sensor debounce sends `CONFIG:DEBOUNCE:<milliseconds>` to
the controller when connected.

## Track Power

The app sends track-power commands over serial. In practice mode it enables the
configured active lanes after an explicit Space start. In heat-race mode it enables only occupied lanes and
cuts power during intermissions and track calls.

Windows acknowledges each controller heartbeat. If acknowledgements stop for
five seconds while a lane is powered, the controller cuts every lane. A
watchdog report pauses a running heat or returns the current qualifier to Ready
for another attempt; routine communication resumption does not itself restore
track power.

Serial/TCP read or write failures, missing heartbeats, and detected controller
resets also interrupt the session even if a watchdog report never arrives.
Reconnection verifies controller identity, heartbeat, and power-off
acknowledgement; the director must press Space to resume. Heat laps and remaining
time are preserved, while an interrupted qualifier must rerun its attempt.
See [Controller recovery](../docs/CONTROLLER_RECOVERY.md) for the complete
workflow, outage timing rules, and hardware checks.

See `..\docs\SERIAL_PROTOCOL.md` for the serial protocol and
`..\docs\TROUBLESHOOTING.md` for Visual Studio and upload recovery notes. See
`..\docs\PIPER_SPEECH.md` for Piper setup and `..\docs\LINUX_SPEECH.md` for
native Linux speech setup under Wine.
