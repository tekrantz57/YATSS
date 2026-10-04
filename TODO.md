# TODO

## Review follow-up and suggested order

Recorded October 3, 2026 after a read-only project review. The existing Release
test runner passed its protocol, lap-race, export, and database tests. Wine,
physical controllers, and complete relay/sensor wiring were not exercised in
that review. The findings below are source-review findings pending targeted
regression tests and hardware confirmation.

1. Bench-validate the implemented communication-loss/controller-reset recovery.
2. Bench-validate the implemented scoring/logging fixes under Wine and sustained load.
3. Implement active race recovery and complete the physical hardware validation
   already listed below before relying on YATSS at a venue.
4. Bench-validate the implemented shared controller core on ESP32 and UNO Q.
5. Settle the distance-format scoring rules, then implement its data model,
   director workflow, reports/exports, and separate live grid.
6. Keep sector timing as a later hardware and scoring project.

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
  race journal; crash recovery remains separate work below.
- See [Serial logging](docs/SERIAL_LOGGING.md) for limits and failure behavior.

## Active race crash recovery

- Persist a transactional race journal or checkpoint after accepted laps,
  manual adjustments, heat transitions, qualifying transitions, and relevant
  configuration changes.
- On startup, detect an unfinished event and offer to resume it or archive and
  discard it. Recovery must preserve controller timestamps, lane rotations,
  stoppage time, qualifying results, and the report audit trail.
- Exercise recovery after forced app termination, Windows restart, controller
  reset, and power loss before relying on YATSS for long endurance races.

## Track-power fail-safe behavior

- Implemented policy: controller boot/reset and loss of Windows communication
  cut every lane. Track-power GPIOs are configured before serial startup, and a
  five-second command watchdog cuts power if Windows keepalives stop.
- Bench-test watchdog trips, reconnects, controller resets, and relay polarity
  with the production controller and relay hardware.
- Decide whether a normally open safety contactor or independent hardwired
  interlock is required. The current normally closed relay wiring cannot remain
  power-off when the controller or relay-coil supply itself loses power.

## Continuous integration

- Implemented first-pass GitHub Actions workflow for the Windows solution and
  protocol/lap-race test runner.
- Added October 4, 2026: common native firmware tests, generated-source drift
  checks, and pinned-core compile jobs for Nano ESP32, C5 N16R8, C6 N4/N8, and
  UNO Q. Confirm the new jobs run successfully on GitHub after pushing.
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
  Existing overnight/lane-1 results predate the refactor. Rebuild release
  firmware packages before publishing; checked-in images do not follow source
  changes automatically.

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

## Future distance-based championship race format

- Add a distinct, selectable race format; leave the existing fastest-lap
  qualifying and heat-race scoring unchanged.
- Design the workflow and standings grid independently. Implement the requested
  capabilities without copying another application's code, artwork, wording,
  or screen layout.
- Qualify each racer by distance covered during a timed run (one minute by
  default), rank by that distance, and carry the credited qualifying distance
  into the first heat and the final combined race total. Show qualifying,
  racing, and combined distances separately in results and exports.
- Introduce a distance-scoring data model that stores completed laps, estimated
  partial-lap distance, director-approved partial distance, and qualifying
  credit separately. Define the scoring unit/precision before implementation;
  the current integer lap count cannot represent an official fractional score.
- At the end of a qualifying run, estimate the unfinished-lap distance from
  elapsed active time since the last crossing and recent valid lap pace. Show
  the estimate to the race director and let them correct it to the car's
  observed track position before the qualifying result is finalized. The
  start/finish sensor cannot measure this fraction directly; define the
  track-section/measurement scale, behavior with too few valid laps, and
  tie-break rules before implementing this format.
- Retain a correction audit trail with the original value, replacement value,
  time, and reason. Preserve estimated and approved values in recovery data,
  reports, JSON, and CSV so the official result can be explained later.
- Decide whether partial distance is credited after each heat or only at
  qualifying/race completion, where cars physically restart, and how the next
  sensor crossing is scored. Prevent the same partial lap from being credited
  twice after a lane change or heat transition. Cover these rules with tests
  before enabling the format.
- Add a separate live standings grid for this format, alongside the existing
  lane board. Show current order, racer/lane, qualifying credit, race distance,
  combined distance, and an explicitly labeled estimated final distance.
- Base each estimate on observed pace and the racer's remaining scheduled
  driving time; account for lane rotations and track calls, and avoid implying
  precision before enough laps have been recorded.
- Start with a simple average pace that includes slow laps and unscheduled
  stops but excludes track-call time. Include future heats in each racer's
  remaining driving time, freeze projections during intermissions, and keep
  projected totals visibly distinct from official credited distance.
- Give each racer a stable identity and publish a consistent race snapshot for
  the separate grid. Use the same scoring values for recovery, reports, and
  exports; avoid deriving official scores from display text or racer names.
