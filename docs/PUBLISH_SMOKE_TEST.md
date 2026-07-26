# Windows Publish Smoke Test

This runbook verifies that the published YATSS Windows application works
outside the development environment. It covers publishing, first launch,
settings persistence, a complete demo race, exported reports, controller
communication, and track-power behavior.

## Prerequisites

- A clean Windows account, Windows Sandbox, or another Windows computer is
  preferred for the first-launch test.
- The hardware portion requires a supported YATSS controller with the current
  sketch installed.
- Keep the controller powered while testing loss of Windows communication.
  Removing controller or relay-coil power is a different electrical test and,
  with normally closed relay contacts, may restore track power.

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
- [ ] The timing board and status-strip layout have consistent borders.
- [ ] Configure opens without an error.
- [ ] Heat length accepts values through 1,440 minutes.
- [ ] A 24-hour heat displays `24:00:00` before it starts.
- [ ] Closing YATSS leaves no YATSS process running.

On first launch, YATSS should create:

```text
%LOCALAPPDATA%\YATSS\laps.db
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

1. Start `Demo Race`.
2. Start `Demo Lap Stream`.
3. Run the race through its final heat.
4. Let the race finish normally.

Verify:

- [ ] Lap counts, last lap, best lap, median, and speed update normally.
- [ ] Ordinary accepted laps do not replace the bottom status message.
- [ ] Demo lanes do not produce false missed-frame warnings.
- [ ] Heat transitions, pauses, resumes, and completion work.
- [ ] The final HTML report window appears automatically.
- [ ] The report includes qualifying details when qualifying was used.
- [ ] The report includes any manual lap adjustments.

Expected report location:

```text
%USERPROFILE%\Documents\YATSS Race Reports
```

For one completed race, confirm creation of:

- [ ] `HeatRace_yyyyMMdd_HHmmss.html`
- [ ] `HeatRace_yyyyMMdd_HHmmss.json`
- [ ] `HeatRace_yyyyMMdd_HHmmss_results.csv`
- [ ] `HeatRace_yyyyMMdd_HHmmss_laps.csv`
- [ ] `HeatRace_yyyyMMdd_HHmmss_qualifying.csv`
- [ ] `HeatRace_yyyyMMdd_HHmmss_adjustments.csv`

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
- [ ] The heat completes and produces its reports.

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

Reset the controller while Windows is not commanding track power:

- [ ] Track-power GPIOs enter the cut state before the serial startup delay.
- [ ] Track power remains cut until Windows sends an explicit power command.

Completely remove controller or relay-coil power as a separate test:

- [ ] Actual relay behavior matches the documented normally closed wiring.
- [ ] Any need for a normally open safety contactor or independent interlock is
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
