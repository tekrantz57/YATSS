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
  are caught on each push.

## Optional VS Code support

- Verified July 24, 2026: the Windows solution builds successfully in VS Code
  with the C# Dev Kit extension on the new development computer.
- Add checked-in `.vscode` recommendations and tasks for building, testing, and
  running the Windows app, plus compiling the controller firmware.
- Document VS Code with C# Dev Kit as a lightweight development option while
  retaining full Visual Studio for WinForms visual-designer work.

## Possible ESP32-C6 support

- If ESP32-C6 support is adopted, separate the controller pin assignments into
  board-specific configurations. The current Nano ESP32 sensor and track-power
  pin arrays are already centralized and do not need restructuring beforehand.
