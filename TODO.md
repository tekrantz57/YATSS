# TODO

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
- Add firmware compile jobs for `YATSSMC` after the .NET workflow is proven on
  GitHub. Start with `arduino:esp32:nano_nora`, then add the ESP32-C5 and
  ESP32-C6 profiles with pinned board-package versions so firmware regressions
  are caught on each push.
- Consider caching NuGet and Arduino board packages if workflow time becomes
  noticeable.

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
