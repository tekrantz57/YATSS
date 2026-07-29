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

## Complete backup schema validation

- Extend database restore validation beyond SQLite integrity, foreign keys,
  schema version, and the `users` table. Verify every required YATSS table and
  column before accepting a backup.
- Add tests proving that partial or falsely versioned databases are rejected
  without replacing the active database.

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

- Add a GitHub Actions workflow that builds the Windows solution and runs the
  protocol and lap-race tests.
- Compile `YATSSMC` for `arduino:esp32:nano_nora` in CI so firmware regressions
  are caught on each push. Also compile the ESP32-C6 profile with
  `esp32:esp32:esp32c6`.

## Optional VS Code support

- Verified July 24, 2026: the Windows solution builds successfully in VS Code
  with the C# Dev Kit extension on the new development computer.
- Add checked-in `.vscode` recommendations and tasks for building, testing, and
  running the Windows app, plus compiling the controller firmware.
- Document VS Code with C# Dev Kit as a lightweight development option while
  retaining full Visual Studio for WinForms visual-designer work.

## ESP32-C6 controller validation

- Implemented compile-time pin profiles for ESP32-C6 and Arduino Nano ESP32.
- Bench-test all eight sensor inputs and all eight track-power outputs on the
  ESP32-C6-DevKitC-1 before committing to the production wiring harness.
- Verify controller diagnostics, watchdog cuts, reset behavior, and sustained
  serial traffic through the CP2102N `UART` connector.
- Exercise in-app firmware installation on both a blank C6 and an already
  programmed C6, including interrupted or failed uploads and serial recovery.

## Arduino Nano ESP32 in-app firmware updates

- Extend the board-neutral `.yatssfw` package manifest and updater UI with an
  Arduino Nano ESP32 package and its required bootloader/upload backend.
- Preserve board-identity matching so a C6 image can never be offered to a Nano
  and vice versa.
- Validate first-time provisioning and updates on physical Nano ESP32 hardware
  before enabling the package in release builds.

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
