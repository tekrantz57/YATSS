# TODO

## Current priorities

Updated October 16, 2026. There are no production installations yet. Implemented
items below retain their validation gaps; historical demo runs are not a
substitute for physical sensor/relay testing or validation of newer code.

1. Bench-validate normally open relay wiring, pulldowns, boot/reset behavior,
   watchdog cuts, and control-power loss/restoration before enabling track power.
2. CI confirmed October 10: all seven jobs passed for `291ceb5`, including
   Linux native tests and all five board builds. Local MSVC setup repaired;
   all 6,722 controller checks pass. Verify the newly added Windows/MSVC CI
   step and current changes after the next push.
3. Bench-validate controller-loss and active-event recovery on Windows and Wine,
   including real sensor traffic, remaining time, and pending approvals.
4. Validate sustained scoring/logging and the shared controller core on both
   controller families; complete each board's hardware checklist below.
5. Validate Combined Distance Racing and revisit its working rules with the
   venue owner. Validate the new ordinary Independent Groups option as well.
6. Bench-validate the newly implemented archive browser and local UNO Q update
   helper; defer remote UNO Q flashing and sector timing until the current
   hardware/scoring/recovery workflows are proven.

## Packaged Software Regression

Packaged software regression implemented October 14: self-contained x64/ARM64
test-package builder, Windows/Wine launchers, separate suite processes,
retained results/logs, failure/timeout reporting and isolated test data. See
[Software validation](docs/SOFTWARE_VALIDATION.md).
- Verified October 14: all nine packaged suites passed on native Windows x64
  and UNO Q ARM64 Wine 10.0 (Debian), both using .NET 10.0.12 and build
  `v0.20.0-beta.2-13-ged2ef62-dirty`. The Wine run completed in about 72 seconds.
- Later October 14 Windows validation passed all eleven suites, including
  archives and the UNO Q updater client. All nine mocked Python helper tests
  passed as well. The earlier Wine regression run predates these two new suites;
  rerun the expanded package on Wine. The first actual local UNO Q update was
  user-verified October 15, as recorded below.
- Director UI acceptance, sustained runs and
  physical tests remain separate work; this result is not hardware certification.

## TCP Controller Software Tests

- Implemented October 15: portable loopback TCP test controller, distinct
  simulator identity, virtual power only, console fault controls, and isolated
  real-app interactive launcher. See [TCP simulator](docs/TCP_CONTROLLER_SIMULATOR.md).
- All thirteen Windows regression suites pass, including real app scoring and
  recovery through sockets: disconnects, silence, resets, stale sessions and
  qualifying reruns. These tests caught and fixed qualifying elapsed-time
  underflow when the last confirmed heartbeat predates the estimated start.
  Normal clock wrap remains covered. Console and isolated UI launchers also
  pass startup/shutdown smoke checks.
- Run the expanded thirteen-suite package and interactive launcher on ARM64
  Wine. Physical MCU, sensor, relay and RouterBridge checks remain separate.

## Optional Windows Installer

- Implemented October 16: optional per-user Inno Setup installer and repeatable
  x64/ARM64 self-contained build script. ZIPs remain available; use ZIPs for Wine.
  Bundled firmware, helpers and guides are copied, never executed by setup.
- Start-menu entry, optional desktop shortcut, build version, stable upgrade
  identity and uninstall are included. Data is not installed/removed; uninstall
  preserves race settings, journals, reports, backups and unowned app-folder files.
- Setup/uninstall refuses the running-app marker and does not automatically
  close/restart YATSS. No drivers, controller flashing, runtime downloads or
  Wine/Linux service configuration. No existing installations need migration.
- Separate test identity supports install/reinstall/uninstall smoke checks.
  Native x64 smoke checks passed October 16: running-app setup/uninstall refusal,
  file replacement on reinstall, shortcuts/registration and preservation of
  external test data and unowned files. Runtime license notices are bundled.
  Verify interactive wizard, newer-version upgrade, installed-app operation and
  native Windows ARM64 behavior before a public installer release. Code signing
  is not configured. See [Windows installer](docs/WINDOWS_INSTALLER.md).

## Installer Help and Discoverability

- Implemented October 13, 2026: Help menu, offline packaged guides, quick start,
  commissioning worksheet, bug-report link, copyable non-private version summary,
  and About. Installer checks complement the developer smoke test, not certify
  hardware safety or physical timing accuracy.
- Race setup now labels Race format and gives it a full row. Combined Distance
  displays qualifying order instead of provisional lanes/rotation queue.
- Automated checks cover menu dispatch, version fields, offline viewer and guide
  packaging; Windows layout checks cover both formats at minimum size. Verify
  Wine viewer/clipboard/layout before venue use.
- Implemented October 15: explicit Copy Selection and Copy All controls, visible
  selection when focus moves to the toolbar, and Open Original for the packaged
  Markdown file in the system's associated application. Clipboard/open failures
  show a warning without closing the guide. Verify native clipboard and document
  associations under Wine; these depend on the desktop environment.

## Communication loss and controller clock resets

- Implemented October 4, 2026: read/write failures, heartbeat loss, watchdog
  reports, and detected controller resets interrupt physical racing. Countdown
  callbacks and automatic intermission starts are cancelled, and reconnect
  holds power off until identity, heartbeat, and power-off acknowledgement are
  verified. The director must press Space to continue.
- Implemented timing policy: freeze heats at the last confirmed controller
  timestamp, retain lap history and remaining time, and rerun an interrupted
  qualifier while preserving earlier qualifying results. Log outage timing
  uncertainty; do not time the first post-interruption crossing as a fast lap.
- Implemented a fresh clock/sequence baseline on backward heartbeat timestamps
  or explicit reset notifications. Ordinary timestamp wrap and reconnect to an
  MCU that kept running do not reset the clock.
- Implemented October 15: protocol-5 controller timing-session identifiers in
  the shared core and app, regenerated at boot and logical RESET. Responses and
  energizing commands are session-bound; retired-session frames are ignored,
  unfamiliar sessions trigger verification, and resumes remain director-controlled.
  Bench-test missed reset notices, same-session reconnects, UNO Q logical resets,
  actual power cycles, and app/firmware/helper deployment together before track use.
- Software verification October 15: all thirteen Windows regression suites,
  nine mocked UNO Q helper tests, and 6,722 shared C++ checks pass. UNO Q
  compiles with hardware-backed session randomness. These are not physical
  reset/power-cycle or Wine UI validation of the new protocol.
- Rebuilt firmware `0.20.0-beta.2-session.1` for Nano, C5/N16R8, C6/N4, and
  C6/N8; verified all package image lengths and SHA-256 hashes. UNO Q source
  compiles and its App Lab import ZIP was regenerated. Automated builds/tests
  did not flash hardware; the user's subsequent successful UNO Q update is
  recorded below.
- Added regression coverage for readiness/explicit resume, lost watchdog
  notifications, heartbeat deadlines, confirmed-time pauses, retained lap
  history, post-reset sequence handling, timestamp wrap, qualifying reruns, and
  countdown cancellation. Bench-test actual USB/TCP reconnects and failures
  during countdowns/intermissions under Windows and Wine before track use.
- See [Controller recovery](docs/CONTROLLER_RECOVERY.md) and
  [Publish smoke test](docs/PUBLISH_SMOKE_TEST.md) for the workflow and checks.

## Final totals after downward lap corrections

- Implemented October 4, 2026: final standings use the latest recorded
  cumulative total for each racer, not the maximum from an earlier heat.
- Regression coverage reduces a racer's total from 10 to 9 in a later heat,
  rotates that racer out, and verifies final order, HTML, JSON, and CSV totals
  and the negative-adjustment audit trail.

## Logging reliability and long-running sessions

- Implemented October 4, 2026: disk and debug-listener writes run on a background
  worker with a bounded queue. Logging failures do not escape into timing;
  storage retries back off for 30 seconds. The main status line reports failures
  and the log window retains an unavailable/dropped-entry indication.
- Implemented midnight rollover, a 30-calendar-day diagnostic-log retention
  policy, and safe log-window read failures. Regression tests cover simulated
  disk-full/access-denied errors, real directory creation failure, retry,
  throwing listeners/warning callbacks, stalled writers, queue overflow,
  oversized entries, retention, and midnight rollover.
- Bench-check log-window rollover and errors under Windows/Wine and run a
  sustained session. Logs are best-effort diagnostics, not the authoritative
  race journal; active-event recovery uses the separate journal described below.
- User-reported October 5, 2026: overnight demo practice remained running on
  Windows and ARM64 Wine at over 10,000 laps per lane, with no suspicious
  behavior on either machine. This exercised the app, not physical MCU capture
  or relay behavior, and predates Combined Distance implementation.
- See [Serial logging](docs/SERIAL_LOGGING.md) for limits and failure behavior.

## Active race crash recovery

- Implemented October 9, 2026: transactional ActiveRace.db journal, durable
  crossings/decisions, compact checkpoints, and startup Resume / Archive and
  Discard / Close choices. Restore holds power off and retains time, rotations,
  completed qualifiers, pending distance approvals, and export completion.
- Automated tests cover forced termination, uncommitted-write rollback,
  locked storage, ownership, compaction, corrections, rotations, approvals,
  interrupted qualifiers, and unsupported schemas.
- See [Active event recovery](docs/ACTIVE_RACE_RECOVERY.md) for workflow and limits.
- Implemented October 10: restart-safe exports use a journaled export plan,
  stable event-specific paths, staged/flushed atomic file replacement, and a
  last-published completion receipt. Tests force termination at every publish
  boundary, exercise locked files, and verify retries, receipts, option retention,
  and same-second event separation. No indivisible multi-file transaction claimed.
- Implemented October 10: stopped current-heat rollback/rerun, reason and
  discarded-attempt audit, full-duration restart, preserved prior heats and
  qualifying credit, and journal/HTML/JSON/CSV coverage. Check dialog, cancellation
  and explicit restart under Windows/Wine. See [Heat reruns](docs/HEAT_RERUNS.md).
- Implemented October 14: Data > Race Archives browser, metadata/details,
  completed-report reconstruction into unique export-copy folders, and confirmed
  individual/date-based deletion. No active-event replacement, original-file
  deletion or automatic retention. See [Race archives](docs/RACE_ARCHIVES.md).
- Earlier-heat reopening and revising already exported races remain outside
  the rerun/archive workflows. Verify archive UI and regenerated reports on Wine.
- Exercise recovery after forced app termination, Windows restart, controller
  reset, and power loss before relying on YATSS for long endurance races.

## Track-power fail-safe behavior

- Implemented policy: controller boot/reset and loss of Windows communication
  cut every lane. Track-power GPIOs are configured before serial startup, and a
  five-second command watchdog cuts power if Windows keepalives stop.
- Bench-test watchdog trips, reconnects, controller resets, and relay polarity
  with the production controller and relay hardware.
- Changed October 9, 2026: normally open 30-87 wiring and active-high run output
  shared by ESP32 and UNO Q, with an external 10 kOhm gate pulldown documented.
  This new hardware arrangement is not yet bench-validated. See
  [Relay wiring](docs/RELAY_WIRING.md); verify matching wiring and firmware
  with track power disconnected. No installations exist yet.
- Validate every lane during boot/reset, controller disconnection, coil-power
  loss/restoration, watchdog trips, and prolonged coil energization. Confirm
  DC motor-load/contact/socket ratings and 3.3 V MOSFET drive adequacy.
- Decide whether an independent safety contactor/hardwired interlock is required;
  normally open contacts alone cannot protect against welded contacts or a
  shorted MOSFET driver.

## Continuous integration

- Implemented first-pass GitHub Actions workflow for the Windows solution and
  protocol/lap-race test runner.
- Added October 4, 2026: common native firmware tests, generated-source drift
  checks, and pinned-core compile jobs for Nano ESP32, C5 N16R8, C6 N4/N8, and
  UNO Q. Confirmed October 10: all seven jobs passed in
  [run 37996620059](https://github.com/tekrantz57/YATSS/actions/runs/37996620059)
  for commit `291ceb5`; no firmware compile jobs were skipped.
- October 9: new relay-output tests cover all 256 masks. The local native test
  launcher could not find `cl`; the inspected toolset lacks headers. Do not
  count the successful Arduino builds as a successful native regression run.
- October 10: Linux native regression run is confirmed green on GitHub.
  Added native MSVC tests to the Windows job and made local compiler detection
  require the registered C++ tools. Installed missing MSVC x64/x86 components
  and Windows SDK 10.0.26100; the local native run now passes all 6,328 checks
  with warnings treated as errors. The new MSVC CI step and today's changes
  still need verification on GitHub after pushing.
- Consider caching NuGet and Arduino board packages if workflow time becomes
  noticeable.

## Shared controller code

- Implemented October 4, 2026: `Controller/YatssController.h` owns common
  framing, commands, ISR capture/debounce/sequence state, bounded queues,
  heartbeat, diagnostics, relay-pulse cancellation, and watchdog safety.
  Both sketch adapters retain their original pin maps and transports; UNO Q
  also retains its status matrix. All platforms timestamp in ISRs.
- One canonical firmware version and generated self-contained sketch copies
  preserve IDE/CLI/App Lab builds. Synchronization checks run in package scripts,
  MSBuild, and CI. ESP32 still reboots on RESET; UNO Q retains MCU uptime.
- Common native tests cover both queue sizes, all lanes, checksums/commands,
  debounce, diagnostics, relay cancellation, overflow, reset, and clock wrap.
  All five supported board configurations compile locally. See
  [Shared controller core](docs/SHARED_CONTROLLER.md) for changes and test tools.
- Bench-test this refactor on both controller families before deployment:
  high-speed capture, coalesced diagnostics, relay pulses superseded by OFF or
  watchdog, mode-boundary queue flushing, resets, and repeated reconnects.
  Existing overnight/lane-1 results predate the refactor. All four bundled
  ESP32 firmware packages and the UNO Q import archive were rebuilt October 9
  for normally open operation. Rebuild again after future firmware changes;
  checked-in images do not follow source changes automatically.

## ESP32-C6 controller validation

- Implemented compile-time pin profiles for ESP32-C6 and Arduino Nano ESP32.
- Bench-test all eight sensor inputs and all eight track-power outputs on the
  ESP32-C6-DevKitC-1 before committing to the production wiring harness.
- Verify controller diagnostics, watchdog cuts, reset behavior, and sustained
  serial traffic through the CP2102N `UART` connector.
- Exercise in-app firmware installation on both a blank C6 and an already
  programmed C6, including interrupted or failed uploads and serial recovery.
- Bench-test automatic N4/N8 selection on physical N4 hardware. Protocol-v4
  firmware reports runtime capacity; older or blank C6 boards use a read-only
  uploader probe, and the flasher probes again before writing.

## Waveshare ESP32-C5 controller validation

- Implemented the compile-time N16R8 pin profile and packaged 16 MB merged
  firmware image for the Waveshare ESP32-C5-WIFI6-KIT-N16R8.
- Bench-test all eight sensor inputs and all eight track-power outputs before
  building a production wiring harness.
- Verify controller diagnostics, watchdog cuts, reset behavior, and sustained
  serial traffic through the CH343 UART connector. The current mapping uses
  GPIO13/GPIO14, so native USB must remain disconnected.
- Exercise in-app firmware installation on both a blank C5 and an already
  programmed C5, including chip/capacity refusal, interrupted uploads, and
  serial recovery.

## Arduino Nano ESP32 firmware-update validation

- Bench-test in-app DFU updates from current YATSSMC firmware and from the Nano
  recovery mode entered by double-tapping RESET.
- Exercise failed and interrupted DFU transfers and confirm that recovery mode
  remains available and YATSS reconnects to the configured COM port afterward.
- Document and bench-test restoring an erased Nano factory recovery partition
  with Arduino tooling; normal in-app DFU updates cannot recreate it.

## Arduino UNO Q production startup

- Added and documented a Linux `systemd` user service and foreground
  supervisor that starts the YATSS App Lab app through `arduino-app-cli`,
  checks TCP port 45991, and restarts the app if the endpoint disappears.
- Verify automatic recovery of the STM32 RouterBridge and TCP port 45991 after
  a cold boot, orderly reboot, container failure, and interrupted startup.
- Verify repeated YATSS stop/restart TCP reconnects without restarting the App
  Lab controller application.
- Bench-test the source-directory stale-app checks and journal output on UNO Q.
- Implemented October 14: local UNO Q updates initiated by YATSS under Wine
  through an authenticated loopback-only Linux helper, with source backup,
  stop/install/start ordering, and identity/heartbeat/power-off verification.
  See `docs/UNO_Q_UPDATES.md`. Complete boot-supervisor variant checks, compile
  failure, helper/YATSS disconnect, and recovery testing before
  relying on it. Remote Windows updates and blank-MCU provisioning remain deferred.
- User-verified October 15 on georgena: local update from ARM64 Wine completed,
  power remained off and heartbeats returned. The configured source directory
  is `/home/arduino/ArduinoApps/yatss-unoq-applab`. Failure/interruption and
  supervisor-specific recovery checks remain pending.
- Bench-test sensor inputs for lanes 2-8, track-power outputs, watchdog
  behavior, relay polarity, and high-speed edge capture with the onboard status
  matrix enabled before using the UNO Q controller on a physical track. Lane 1
  D2 sensor input has passed an end-to-end physical test.

## Future Formula 1-style sector timing

- Consider two optional intermediate sensors per lane, producing three sector
  times within each completed lap. Sector hardware would default to not
  installed so the existing single start/finish sensor remains sufficient.
- Include sector times in heat-race and qualifying reports plus JSON and CSV
  exports when sector timing is enabled.
- Define behavior for missing, duplicate, and out-of-order sector events before
  implementation.
- Evaluate input-expansion hardware before assigning pins. Eight lanes with
  three sensors per lane plus eight track-power outputs cannot all connect
  directly to the current ESP32-C6 GPIOs.

## Starting-lane selection for separate racer groups

- Implemented for Combined Distance and, October 13, for ordinary heat races
  using the Independent Groups scheduling option. Ordinary Rotation Queue
  remains available and offers starting choices only to its first lane-count
  racers; this is a separate supported scheduling choice, not a missing feature.
- Groups use consecutive qualifying ranks, strongest first, with full groups
  followed by the remainder (eight and two for ten racers on eight lanes).
  Each group chooses independently in qualifying order, removing selected lanes
  from that group's available choices, then completes its full lane rotation.
- Ordinary groups rank by fastest-lap qualifying; Combined Distance ranks by
  director-approved qualifying distance. Choices are initial lanes, not new
  choices at every segment. Each new group requires an explicit director start.
- Keep recovery/rotation/report coverage and validate the director workflow on
  Windows/Wine. Venue-owner review, alternative balancing and advancement remain
  future work. See [Independent heat groups](docs/INDEPENDENT_HEAT_GROUPS.md)
  and [Combined Distance Racing](docs/COMBINED_DISTANCE.md).

## Combined Distance Racing validation and follow-up

- Rules comparison recorded October 5, 2026: see
  [Timing and scoring rules comparison](docs/TIMING_SCORING_RULES_COMPARISON.md).
  Revisit final tie-breaks, group balancing/run order, and assigned versus chosen
  initial lanes with the venue owner. Second-based heat/segment durations were
  implemented October 10, 2026; bench-test short segments with real sensors.
  Consider backup-lap qualifying ties, segment reruns, and penalty workflows
  before claiming sanctioned-format support. Current local policies are
  unchanged; current USRA rules still need verification.
- First version implemented October 5, 2026: integer-hundredths scoring,
  mandatory qualifying/final confirmations, consecutive independent groups,
  group-local starting-lane choices, a separate Live Standings window with
  projections, and HTML/JSON/CSV distance results and confirmation audits.
- See [Combined Distance Racing](docs/COMBINED_DISTANCE.md) for the operator
  workflow, tie and estimate defaults, export schema, and current limits.
- Bench-test start-line pulses, full rotation, equivalent-position car moves,
  short-group power masks, track calls, and interrupted qualifiers under
  Windows and Wine. Active-race recovery is implemented; bench validation remains pending.
- Implemented October 13: ordinary heat races offer Independent Groups alongside
  the existing Rotation Queue. Fastest-lap qualifying, group-local lane choices,
  strongest-first full groups plus remainder, full rotations, explicit group
  starts, recovery and reports are supported. See
  [Independent heat groups](docs/INDEPENDENT_HEAT_GROUPS.md). Bench verification
  under Windows/Wine remains pending; group balancing/advancement are not added.

- The implemented naming, hundredths precision, qualifying carry, first-crossing
  rules, no intermediate fractions, independent groups, confirmations, tie
  defaults, projection policy, stable racer IDs, and export audits are documented
  in [Combined Distance Racing](docs/COMBINED_DISTANCE.md). Keep that guide as
  the current behavior reference rather than duplicating completed design tasks
  here. All working rules remain subject to venue-owner input; update the guide
  and regression tests together when those decisions change.
- Preserve an independently designed workflow and UI. Do not copy another
  application's code, artwork, wording, or screen layout, or imply sanctioned
  compliance before verifying applicable rules and hardware behavior.
