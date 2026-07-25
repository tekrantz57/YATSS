# TODO

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
