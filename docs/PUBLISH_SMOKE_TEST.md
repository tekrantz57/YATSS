# Windows Publish Smoke Test

This runbook verifies that the published YATSS Windows application works
outside the development environment. It covers publishing, first launch,
settings persistence, a complete demo race, exported reports, controller
communication, and track-power behavior.

## Active event recovery checks

- [ ] Terminate a demo heat mid-race; restart and restore. Verify totals,
  history, lane assignments, and remaining time; power stays off until Space.
- [ ] Restore an interrupted qualifier: rerun it, retaining earlier results.
- [ ] Restore pending Combined Distance fraction approvals and lane choices;
  verify no qualifying carry or final fraction is applied twice.
- [ ] Restore during intermission; verify no automatic next-heat start.
- [ ] Repeat with real sensors on Windows and ARM64 Wine, including OS restart,
  controller reset, and power loss. Reconcile outage crossings explicitly.
- [ ] Exercise storage failure/backlog safely with track power disconnected;
  verify stopped racing, visible warning, and last committed recovery.
- [ ] Verify final exports and startup archive behavior, and Close YATSS leaves
  unfinished recovery untouched. See [Active event recovery](ACTIVE_RACE_RECOVERY.md).

## Current-heat rerun checks

- [ ] In a demo race, pause a later heat, make a correction, then choose
  Mode > Rerun Current Heat. Cancel: totals remain and automatic start stays paused.
- [ ] Confirm with a reason: previous heats and qualifying credit stay intact;
  current laps/corrections disappear; lane assignments do not change; full
  duration returns. Verify power off and explicit Space/countdown restart.
- [ ] Repeat from completed-heat intermission and rerun twice. Verify numbered
  audits, clean lap statistics and correct first-crossing behavior.
- [ ] Restart after rollback: Ready, full duration, restored totals and audit.
- [ ] Check Combined Distance: earlier groups' approvals remain; current-group
  finish approvals require confirmation again after its rerun.
- [ ] Verify HTML/JSON/CSV audit and absence of discarded laps from official
  results. Check disabled menu in practice, qualifying, running heats,
  countdowns, approval dialogs and after final exports start.
- [ ] Check confirmation layout and cancellation under Windows and ARM64 Wine.
  See [Current-heat reruns](HEAT_RERUNS.md). Physical car positioning remains
  a director responsibility.

## Help and Setup Checks

- [ ] From the copied published folder, disconnect internet and open every
  Help guide. Verify the quick start, installer checklist, pin maps, wiring,
  troubleshooting, reruns and exports are readable locally.
- [ ] Copy Version Information and inspect app/controller identity, OS,
  runtime and architecture. Verify it contains no racer names, personal paths
  or connection addresses. Controller identity alone is not live readiness.
- [ ] Test About, clipboard availability and Report a Bug. If a guide is
  missing, confirm the online fallback can be declined without affecting racing.
- [ ] At the minimum setup window size, verify Race name and Race format fit.
  Switch formats: Combined Distance shows qualifying order without provisional
  lane assignments; Heat Race restores its existing lane grid/rotation queue.
- [ ] Repeat Help/viewer and setup layout checks under ARM64 Wine.

## Independent Heat Group Checks

- [ ] Select Heat Race / Independent Groups. Cancel qualifying setup: verify
  no race start or power-on until qualifying and lane choices finish.
- [ ] Qualify ten racers on eight lanes. Collect separate lane choices for
  ranks 1-8 and 9-10; all lanes become available again for group two.
- [ ] Verify 16 global heats, every racer visiting eight lanes, no waiting-queue
  crossover, and unused lanes off. Group two waits for Space even with a timed
  intermission and starts with zero racing laps/no qualifying credit.
- [ ] Check downward corrections, current-heat reruns, restart during qualifying
  and at a group boundary, and ordinary results across all groups.
- [ ] Verify HTML groups, JSON scheduling/assignments and CSV Group metadata.
- [ ] Switch back to Rotation Queue and verify existing scheduling; switch
  to Combined Distance and verify its fixed grouping and distance approvals.
- [ ] Repeat physical workflow/UI under Windows and ARM64 Wine.

## Race Archive Checks

- [ ] Finish a demo event and restart so it is archived. Open Data > Race
  Archives with power off; inspect search, pages, details and the demo flag.
- [ ] Reopen the original report, then move a copy away to verify the missing
  original message. Export a new copy with JSON/CSV choices and compare totals,
  qualifying, groups, corrections and rerun audits. Originals stay unchanged.
- [ ] Archive an abandoned event and verify it cannot produce final results.
- [ ] While paused in another event, browse and export older events. Verify
  current totals/time/lanes are unchanged; closing does not resume automatically.
- [ ] On disposable test data only, cancel deletion, then delete one archive
  and a date-based batch. Active recovery and exported files must remain intact.
- [ ] Verify unavailable controls while racing/counting down and repeat on Wine.

## Prerequisites

- A clean Windows account, Windows Sandbox, or another Windows computer is
  preferred for the first-launch test.
- The hardware portion requires a supported YATSS controller with the current
  sketch installed.
- Keep the controller powered while testing loss of Windows communication.
  Removing controller or relay-coil power is a separate electrical test: the
  current normally open wiring should leave power off. Verify matching firmware,
  wiring, and gate pulldowns first; see [Relay wiring](RELAY_WIRING.md).

## 1. Create the Published Folder

From the repository root, run:

```powershell
dotnet publish YATSSWin\YATSS\YATSS.csproj `
  -c Release `
  -p:PublishProfile=FolderProfile
```

Expected output folder:

```text
YATSSWin\YATSS\bin\Release\publish\win-x64
```

Copy the entire `win-x64` folder to a location outside the repository. Run only
the copied `YATSSWin.exe` during the smoke test so the application cannot use
files from normal build output.

## 2. Clean Launch

Record each item as pass or fail:

- [ ] `YATSSWin.exe` starts without Visual Studio.
- [ ] The application does not require a separately installed .NET runtime.
- [ ] The YATSS application and taskbar icons are correct.
- [ ] The bottom title panel uses Carolina Blue.
- [ ] The complete version is visible at the lower right of the title panel at
  normal and maximized window sizes, including a long development identity such
  as `v0.20.0-beta.2-3-g1a2b3c4-dirty`.
- [ ] Clean release, intermediate commit, and dirty build identities display
  without clipping at 100%, 125%, and 150% display scaling.
- [ ] The timing board and status-strip layout have consistent borders.
- [ ] Configure opens without an error.
- [ ] Lap-best sounds can be disabled and remain disabled after restarting.
- [ ] Voice announcements can be disabled and remain disabled after restarting.
- [ ] With voice disabled, starting and resuming still show synchronized start
  lights and wait through the silent 1.5-second minimum countdown before
  restoring track power.
- [ ] Re-enabling voice makes the installed voice selector available again.
- [ ] `Automatic`, `Windows SAPI`, `Piper`, `eSpeak NG helper`, and `None` are
  available in the speech-engine selector; changing engines refreshes the
  voice list and selects an available voice.
- [ ] Heat length accepts 1 through 86,400 seconds (24 hours); default is 180.
- [ ] Run 30- and 90-second heats; verify expiration, track-call pause/resume,
  saved setup after reopening, recovery time remaining, and report duration.
- [ ] A 24-hour heat displays `24:00:00` before it starts.
- [ ] Closing YATSS leaves no YATSS process running.

On first launch, YATSS should create:

```text
%LOCALAPPDATA%\YATSS\YATSS.db
%LOCALAPPDATA%\YATSS\logs\
```

- [ ] The database is created.
- [ ] The log directory is created.
- [ ] Changed settings remain after closing and reopening YATSS.

## 3. Demo Race And Reports

First enable both options under `Configure > Race Reports`:

- `Write JSON race archive`
- `Write CSV data files`

Then:

1. Start `Demo Race...`.
2. Confirm that `Simulated Lap Input` is checked automatically.
3. Run the race through its final heat.
4. Let the race finish normally.

Verify:

- [ ] Lap counts, last lap, best lap, median, and speed update normally.
- [ ] The first eligible timed lap of a heat plays two pips, later overall
  heat-best laps also play two pips, and other lane-best improvements play one.
- [ ] Ordinary accepted laps do not replace the bottom status message.
- [ ] Demo lanes do not produce false missed-frame warnings.
- [ ] Practice and Heat Race never show radio marks at the same time.
- [ ] Simulated Lap Input remains an independent checkbox when changing the
  primary mode.
- [ ] Heat transitions, pauses, resumes, and completion work.
- [ ] The final HTML report window appears automatically.
- [ ] The report includes qualifying details when qualifying was used.
- [ ] The report includes any manual lap adjustments.

Expected report location:

```text
%USERPROFILE%\Documents\YATSS Race Reports
```

For one completed race, confirm creation of:

- [ ] `HeatRace_yyyyMMdd_HHmmss_<event-id>.html`
- [ ] `HeatRace_yyyyMMdd_HHmmss_<event-id>.json`
- [ ] `HeatRace_yyyyMMdd_HHmmss_<event-id>_results.csv`
- [ ] `HeatRace_yyyyMMdd_HHmmss_<event-id>_laps.csv`
- [ ] `HeatRace_yyyyMMdd_HHmmss_<event-id>_qualifying.csv`
- [ ] `HeatRace_yyyyMMdd_HHmmss_<event-id>_adjustments.csv`
- [ ] Matching `.yatss-export` completion receipt, with no `.yatss-tmp` remnants.
- [ ] Simulate interrupted/blocked exports in an isolated demo. After fixing
  access, restart and Finish Exports; verify the same filenames, no restarted
  heat, retained export choices, and no additional report copy.

Open the HTML report and inspect it. Validate the machine-readable files with:

```powershell
Get-Content "HeatRace_yyyyMMdd_HHmmss.json" -Raw |
  ConvertFrom-Json |
  Out-Null

Import-Csv "HeatRace_yyyyMMdd_HHmmss_results.csv" |
  Select-Object -First 1
```

- [ ] JSON parsing completes without an error.
- [ ] The results CSV returns a populated row.
- [ ] Racer names, lap totals, and timing values agree with the HTML report.

Disable both JSON and CSV options and complete another short demo race:

- [ ] The HTML report is still created and displayed.
- [ ] No JSON file is created for that race.
- [ ] No CSV files are created for that race.

## 4. Database Backup And Restore

Open `Data > Back Up Database...` and save a manual backup.

- [ ] The completion dialog reports that the backup was verified.
- [ ] The backup appears under `Documents\YATSS Backups`.
- [ ] `Data > Open Database Folder` opens `%LOCALAPPDATA%\YATSS`.
- [ ] `Data > Open Backup Folder` opens the backup directory.
- [ ] An automatic `YATSS-auto-YYYYMMDD.db` exists under `Automatic` after
  startup.
- [ ] Restarting YATSS on the same day does not create a second daily file.

Make a recognizable, reversible settings or racer-name change, then restore the
manual backup from Practice mode.

- [ ] YATSS asks for confirmation and states that it will restart.
- [ ] Track power is cut before restore.
- [ ] A timestamped `YATSS-before-restore-*.db` safety copy is created.
- [ ] YATSS restarts and loads the values from the selected backup.
- [ ] Attempting restore during a configured heat or qualifying session is
  refused.

## 5. Controller And Track Hardware

Connect the controller and select its COM port in Configure.

Before energizing track or relay-coil power, verify firmware installation with
the production ZIP:

- [ ] The publish folder contains C5/N16R8, C6/N4, C6/N8, and Nano `.yatssfw`
  files under `Firmware` and contains neither `esptool.exe` nor `dfu-util.exe`.
- [ ] `File > Update Controller Firmware...` is refused outside idle Practice
  mode, during simulated input, and while Controller Diagnostics is open.
- [ ] With Arduino's ESP32 core installed, YATSS reuses its uploader without a
  download prompt.
- [ ] On a clean Windows account, YATSS asks before downloading the official
  Espressif uploader, verifies it, and caches it under
  `%LOCALAPPDATA%\YATSS\Tools`.
- [ ] An existing C6 is identified before confirmation and a different board
  profile is rejected.
- [ ] Protocol-v4 C6 firmware selects N4 or N8 from its reported capacity.
- [ ] Older and blank C6 firmware is probed read-only and selects the package
  matching the detected capacity.
- [ ] Immediately before writing, a second C6 capacity probe refuses a
  deliberately mismatched package.
- [ ] A physically confirmed blank C6 can be provisioned on its selected COM
  port.
- [ ] An existing C5 is identified as the Waveshare N16R8 profile and selects
  the 16 MB package automatically.
- [ ] A blank C5 is probed as an ESP32-C5 with 16 MB flash before confirmation
  and is provisioned through its CH343 UART COM port.
- [ ] The C5 flasher refuses a different ESP32 family or a capacity other than
  16 MB immediately before writing.
- [ ] An identified Nano selects its Nano package automatically and completes
  a DFU update.
- [ ] A Nano in factory recovery mode can be selected manually and updated
  after double-tapping RESET.
- [ ] On a clean Windows account, Nano update asks before downloading Arduino's
  official DFU utility, verifies it, and caches it under
  `%LOCALAPPDATA%\YATSS\Tools`.
- [ ] After writing, YATSS reconnects and verifies the expected board profile
  and firmware version.
- [ ] Cancelling before write and a deliberately failed probe leave normal
  serial reconnection available.

- [ ] YATSS reports that the controller is responding.
- [ ] The COM-port selection remains after restarting YATSS.
- [ ] Controller Diagnostics opens.
- [ ] Each sensor changes the correct logical lane.
- [ ] Each relay pulse cuts only the intended lane.
- [ ] Cutting all power from diagnostics cuts every lane.
- [ ] Closing diagnostics restores normal edge processing.

Run a short hardware heat:

- [ ] Before Heat 1 starts, the main status band displays
  'PRESS SPACE TO START' in gold.
- [ ] The first edge establishes the expected baseline.
- [ ] Subsequent valid edges count laps.
- [ ] Space pauses the heat and cuts track power.
- [ ] While paused after the track call, the main status band displays
  'PAUSED - PRESS SPACE TO RESUME' in gold.
- [ ] Pressing Space changes the prompt to 'RESUMING...' during the countdown.
- [ ] Space resumes through the countdown and restores the occupied lanes.
- [ ] The Next button on an unmodified Logitech R500s performs the same
  start, track-call, and resume actions as Space.
- [ ] The R500s Back and laser buttons do not operate race control.
- [ ] During a timed intermission, Space pauses the automatic next-heat start.
- [ ] A subsequent Space press starts the next heat through its countdown.
- [ ] The heat completes and produces its reports.

Run a short qualifying session:

- [ ] Space during qualifying cuts track power and pauses the qualifying timer.
- [ ] The main status band prompts for Space to resume qualifying.
- [ ] Space resumes qualifying through the countdown and restores only the
  configured qualifying lane.
- [ ] Qualifying session and lap times exclude the stopped interval.

## 6. Communication Watchdog

This test must interrupt Windows communication while leaving the controller and
relay-coil supply powered.

1. Enable track power in Practice or start a heat.
2. Close YATSS or otherwise stop its serial communication.
3. Observe the track-power relays.

- [ ] Every lane is cut within approximately five seconds.
- [ ] The controller does not restore power by itself.

Restart YATSS:

- [ ] YATSS reconnects to the configured controller.
- [ ] Controller status and diagnostics work after reconnection.
- [ ] Track power follows the new application's explicit command.
- [ ] Startup/reconnect acknowledges power off, receives controller identity and
  heartbeat, and leaves power off until Space starts its countdown.

Interrupt the connection while YATSS remains open, including during a heat,
qualifying, countdown, and intermission:

- [ ] A heat pauses at confirmed controller time and retains its lap totals.
- [ ] An interrupted qualifier is ready to rerun; earlier results remain.
- [ ] Cancelled countdowns and intermission timers cannot later start a heat.
- [ ] Reconnect shows the controller-ready prompt without restoring power.
- [ ] Space resumes through the countdown; the first post-outage crossing does
  not set a fastest lap, and the following complete lap is timed normally.
- [ ] An MCU reset establishes fresh timing/sequence baselines while preserving
  heat totals and remaining time. Normal timestamp wrap does not cause a fault.

Reset the controller while Windows is not commanding track power:

- [ ] Track-power GPIOs enter the cut state before the serial startup delay.
- [ ] Track power remains cut until Windows sends an explicit power command.

Completely remove controller or relay-coil power as a separate test:

- [ ] Each healthy normally open 30-87 contact remains open without coil power.
- [ ] Each gate has an external pulldown; boot/reset/unplugging causes no pickup.
- [ ] Restoring control/coil supplies does not automatically restart the track.
- [ ] Release latency, long-run coil/driver temperature, and DC load ratings pass.
- [ ] Any need for an independent safety contactor or hardwired interlock is
  recorded before installation.

## 7. Result Record

```text
Date:
Tester:
Windows version:
YATSS commit/version:
Controller board:
Controller sketch commit/version:
Track/relay hardware:

Overall result: PASS / FAIL

Failures and observations:

Follow-up work:
```

A publish smoke test passes only when all required software and hardware checks
for the intended installation pass, or when any intentionally deferred hardware
checks are clearly recorded and accepted.

For the smaller package that requires .NET 10, repeat the clean-launch and
demo-race portions on a system with the matching .NET 10 Desktop Runtime.
Confirm that it produces a useful runtime-install prompt, rather than an
unrelated application error, when that runtime is absent.
