# TODO

## Current priorities

Updated October 9, 2026. There are no production installations yet. Implemented
items below retain their validation gaps; historical demo runs are not a
substitute for physical sensor/relay testing or validation of newer code.

1. Bench-validate normally open relay wiring, pulldowns, boot/reset behavior,
   watchdog cuts, and control-power loss/restoration before enabling track power.
2. Confirm CI, including native controller tests; repair the local C++ toolchain
   if local native tests are needed. All five board builds passed October 9.
3. Bench-validate controller-loss and active-event recovery on Windows and Wine,
   including real sensor traffic, remaining time, and pending approvals.
4. Validate sustained scoring/logging and the shared controller core on both
   controller families; complete each board's hardware checklist below.
5. Validate Combined Distance Racing and revisit its working rules with the
   venue owner. Separate-group lane choices for ordinary heat races remain open.
6. Defer archive enhancements, UNO Q in-app flashing, and sector timing until
   the current hardware/scoring/recovery workflows are proven.

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
  MCU that kept running do not reset the clock. Consider a protocol boot-session
  identifier later for resets that cannot be inferred from existing frames.
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
- Consider archive browsing/retention, full-current-heat rollback, and
  exactly-once exports after bench validation of this first version.
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
  UNO Q. Confirm the new jobs run successfully on GitHub after pushing.
- October 9: new relay-output tests cover all 256 masks. The local native test
  launcher could not find `cl`; the inspected toolset lacks headers. Do not
  count the successful Arduino builds as a successful native regression run.
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
- Add an UNO Q firmware-update path initiated by YATSS through a Linux-side
  `arduino-app-cli` helper. It must stop and restart the App Lab runtime safely,
  flash the STM32 sketch, redeploy the bridge, and verify the controller
  identity; the existing controller TCP connection is not a flashing transport.
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

- Combined Distance already implements independent group-local choices.
  Extending that behavior to ordinary timed heat races is still proposed,
  subject to venue-owner input and a decision on group/rotation scheduling.
- Requested October 5, 2026: each group of eight or fewer racers (limited by
  the active lane count) chooses its starting lanes independently. The best
  qualifier within that group chooses first, followed by the remaining racers
  in that group's qualifying order; each choice removes that lane from the
  available choices for that group.
- Apply this to existing timed heat races using fastest-lap qualifying order.
  The current qualifying workflow offers lane choices only to the first
  lane-count racers in the overall ranking and puts the remainder in the
  rotation queue; it does not provide independent choices for separate groups.
- Combined Distance uses director-approved qualifying distance, highest first;
  keep that implemented group-local workflow covered by recovery/rotation tests.
- Grouping agreed October 5, 2026: use consecutive qualifying ranks, 1-8,
  9-16, and so on on an eight-lane track (use the active lane count on smaller
  tracks). No director regrouping. For now, fill each group before starting
  the next; ten racers form groups of eight and two, not five and five.
- These are starting-lane choices, not new choices at every timed segment.
  Keep subsequent lane rotation separate. Define how independent groups are
  scheduled in relation to the existing rotation queue before implementation.

## Combined Distance Racing validation and follow-up

- Rules comparison recorded October 5, 2026: see
  [Timing and scoring rules comparison](docs/TIMING_SCORING_RULES_COMPARISON.md).
  Revisit final tie-breaks, group balancing/run order, fractional-minute segment
  durations, and assigned versus chosen initial lanes with the venue owner.
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
- The separate-group lane-choice change for existing fastest-lap heat races
  remains pending; that format's existing rotation-queue behavior is unchanged.

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
