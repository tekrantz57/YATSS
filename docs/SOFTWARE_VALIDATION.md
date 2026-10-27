# Packaged Software Regression Validation

Run existing automated YATSS tests on Windows or ARM64 Wine. This is not the
actual-app director acceptance worksheet, a physical test,
or timing/safety certification. No real controller is connected or flashed.
WinForms tests require a working graphical session under Wine.

## Testing Strategy

For ordinary changes, run focused tests covering the affected behavior. Expand
coverage when changes affect shared scoring, recovery, protocols, firmware or
other high-risk behavior. Run the full relevant regression suite before a
GitHub release; a commit or push alone does not require a full suite. Record
what was actually tested and keep physical validation separate.

## Build and Run

From the source checkout with the .NET 10 SDK:

```powershell
./tools/Build-ValidationPackage.ps1
```

Unique dated x64 and ARM64 ZIPs are written under `artifacts`, from the same
source/build identity. Both are self-contained: no separately installed .NET
runtime is needed on the test machine. Extract the whole ZIP. For just ARM64,
add `-Runtime win-arm64`. These are test packages, not application releases.

On Windows, open PowerShell in the matching extracted package:

```powershell
./Run-YatssValidation.ps1
```

Optionally set `-ResultsDirectory C:\YATSS-Test-Results` and
`-SuiteTimeoutSeconds 900`. Run as an ordinary user, not administrator.

On ARM64 Linux, extract the ARM64 package and use native graphical Wine:

```bash
bash ./run-yatss-validation-wine.sh
```

The default dedicated prefix is `~/.local/share/YATSS-validation-wine`. Wine
may initialize it on first use; the script does not install/repair Wine or alter
the ordinary app prefix. Use `YATSS_VALIDATION_WINEPREFIX` only for another
dedicated test prefix. Custom installs can set `YATSS_VALIDATION_WINE_COMMAND`
and `YATSS_VALIDATION_WINEPATH_COMMAND` to matching executable paths.

Optional output location and per-suite timeout:

```bash
bash ./run-yatss-validation-wine.sh "$HOME/YATSS-Test-Results" 900
```

Use the x64 package on x64 Wine. An ARM64 package is not an x86/x64 emulator.
Both platforms should test the same source build; Windows success does not
establish Wine compatibility.

## Test Areas

October 25 firmware-notice checks cover numeric/semantic version ordering,
prereleases/build metadata, old/equal/newer/unknown installed versions, exact
board and capacity selection, ambiguous/missing bundles, reconnect notice keys,
and simulator suppression. Isolated regression runs suppress actual startup
dialogs and never flash real controllers.
All fourteen Windows suites passed with the firmware-notice addition; the
Release build completed with zero warnings and zero errors. The real startup
prompt/update flow still needs a manual check with an older physical controller.

October 25 capture-arming coverage adds native interrupt-mask/queue-boundary
checks and real-app TCP scenarios for selected Practice lanes, all-disarmed
capture, missing/stale acknowledgements, fresh rearm baselines and race-mask
restoration. Native checks now total 7,012. These do not electrically verify
detachment on a physical controller.

October 25 verification: all fourteen Windows suites passed, including the
expanded real-app arming tests. Release build had zero warnings/errors and the
Practice chooser was inspected at its minimum size. Nano, C5, C6 N4/N8, Pico
2 W and UNO Q compiled; four ESP32/Nano update packages, Pico UF2 and the UNO Q
source import ZIP were rebuilt. UNO Q was compile-checked with locally installed
Arduino Q core 1.0.0; its pinned CI core remains a separate check. No board was
flashed during this implementation and physical arming remains unverified.

The current development runner contains fourteen suites. The October 19 Lap
Finish addition covers baseline/counting rules, invalid crossings, target
cutoff, controller wrap, pauses, recovery, reruns, exact ties, settings and
minimum-size setup layout, archive reconstruction and HTML/JSON/CSV exports.
The real-app TCP suite also verifies that reaching the target commands virtual
power off, freezes both racers and automatically exports results. It also
exercises the Lap Finish demo menu, countdown and simulated-input setup, then
switches back to the unchanged timed demo preset. These tests do not operate
physical relays or validate Wine on the UNO Q.

October 19 Lap Finish verification: all fourteen isolated Windows suites passed,
followed by a successful complete in-process test run. Release configuration
built with zero warnings and zero errors. The minimum-size Lap Finish setup
screenshot was also inspected for clipping and control overlap.

Separate child processes run runner-reporting checks; Help and packaged guides;
archive browsing/reconstruction/export-copy/retention checks;
UNO Q update bundle/authentication/HTTP completion checks (no hardware flashing);
controller timing-session identity, stale-message rejection, and reset/reconnect checks;
real-app TCP simulator fault/recovery integration (no physical controller);
independent groups; active-event recovery; heat durations/intermission settings;
heat reruns; restart-safe exports; Combined Distance; Lap Finish; and existing core
protocol/scoring/statistics/logging/speech-policy/backup tests. A failure does
not hide subsequent suites. Runner self-checks deliberately fail and time out
nested fixtures; their parent suite passes only if failures are reported correctly.

Native C++ controller tests, Arduino compilation and interactive UI-check modes
remain separate. None is silently counted as passed by this runner.

## Isolation and Results

Before tests access AppDatabase, the regression executable sets and verifies a
process-local test-data path. The normal app sets no override; its paths remain
unchanged. Each suite has its own TestData folder and redirected TEMP/TMP. Crash
fixtures inherit that workspace. Migration/backup tests use fixture files only.
Normal YATSS.db, ActiveRace.db, reports, backups and serial logs are not used.

The default output is `Results` beside the launcher. Each run creates a unique
subfolder containing `summary.txt`, `results.json`, raw suite logs and test data.
Reports update after every suite; an interrupted run remains INCOMPLETE rather
than claiming success. Test data is retained for diagnosis. Delete completed
result folders deliberately when no longer needed, and review paths in logs
before sharing publicly.

PASS requires every suite to exit zero and close its output pipes. FAIL records
the exit code/runtime/assertion output. TIMEOUT is a failure, not a skipped
success. Launchers return nonzero on failure. Default per-suite timeout is 600
seconds; allowed launcher values are 30-3600. Crash-fixture startup allowance is
60 seconds for slower systems, without weakening assertions.

Launchers retain startup/console logs. If Wine/.NET fails before the runner
starts, there will be no results report: inspect that log as a startup failure.
If the runner itself crashes, its last report may be INCOMPLETE. Do not suppress
failing checks to get a green Wine result. A runtime stack trace alone does not
prove whether Wine, .NET or YATSS is responsible.

Timeout cleanup targets test processes only. Wine process-tree termination may
be incomplete; stop any remaining validation process/prefix specifically. Never
indiscriminately stop a wineserver used by a real event.

## Verified Runs

October 14, 2026, before archive-browser additions: all nine suites passed in both self-contained packages for
build `v0.20.0-beta.2-13-ged2ef62-dirty`, using .NET 10.0.12:

- Native Windows x64, reported OS build 10.0.26300.
- Arduino UNO Q ARM64, Debian Wine 10.0 (`10.0~repack-6`), run under the arduino
  desktop account. Completed 16:22:17-16:23:29 Eastern, about 72 seconds.

The Wine report's `Microsoft Windows 10.0.19043` is its emulated Windows identity,
not the host Linux version. Every suite exited zero; core and Help log endings
also contained no stderr diagnostics. This verifies the automated checks, not
all UI workflows, sustained endurance, real communications or physical hardware.
Raw summaries/results are retained as local artifacts, not committed user data.

Later October 14 Windows validation passed all eleven suites, including the new
archive browser and UNO Q update client checks. The Linux helper's separate
mocked Python tests also passed on Windows. These additions have not yet been
run through an actual UNO Q firmware update or its Wine update UI; the earlier
nine-suite Wine result must not be presented as validation of those features.

October 15 session-aware protocol verification passed twelve Windows suites,
including `sessions`, plus nine mocked Python helper tests and 6,722 shared C++
checks. The UNO Q sketch compiles against the pinned Zephyr core with hardware
entropy. Physical resets, power cycles, and the Wine UI still need bench checks
with matching app, firmware, and updater helper.
All four ESP32 packages were rebuilt as `0.20.0-beta.2-session.1` and their
manifest versions, image lengths, and SHA-256 hashes verified. The matching
UNO Q App Lab import ZIP was regenerated. No controller was flashed.

## Run from Source

October 15 TCP simulator addition: all thirteen Windows suites passed. Both the
portable console and isolated real-app interactive launcher passed startup/exit
smoke checks. This includes the qualifying pre-start-heartbeat underflow fix.
The expanded package still needs an ARM64 Wine run. Separately, the user
verified a successful local UNO Q update under Wine with returning heartbeats;
that does not cover update failure/interruption scenarios.
Later October 15, all thirteen suites passed again with explicit Help copy and
original-file controls. Their dispatch and exact copied text are covered by
automated checks; minimum-size Windows toolbar layout was captured and checked.
Wine clipboard integration and Markdown file associations remain desktop checks.

The thirteen-suite runner now includes `tcp`, which launches the real app
against an isolated loopback controller. Interactive testing is available with
`YATSS.Tests.exe --tcp-test-ui`; see [TCP test controller](TCP_CONTROLLER_SIMULATOR.md).

```powershell
dotnet run --project YATSSWin/YATSS.Tests -c Release -- --validation ./artifacts/validation-results
```

The existing no-argument full suite still works and now isolates test app data.
Temporary fixtures use a separate short, unique directory under the system
temporary folder so long result-folder names do not exhaust SQLite's native
Windows backup-path limit. This does not change production database paths.
Named `--regression-suite` entry points are internal. Crash/runner fixtures may
deliberately wait indefinitely or fail; do not launch them manually.
