# TODO

## Hardware diagnostics

- Add a controller diagnostic mode that reports the current state of every
  sensor input.
- Add a deliberate, one-at-a-time relay test so lane assignments and relay
  driver wiring can be verified safely.

## Track-power fail-safe behavior

- Define the intended track-power state during controller boot, reset, Windows
  application failure, and serial disconnection.
- Configure the track-power GPIOs before serial initialization and other
  startup delays if power must remain cut throughout boot. With the current
  normally-closed relay wiring, track power may be present until the GPIOs are
  configured.
- Consider a controller watchdog that cuts track power when communication with
  the Windows application is lost, if that matches the desired operating
  policy.

## Continuous integration

- Add a GitHub Actions workflow that builds the Windows solution and runs the
  protocol and lap-race tests.
- Compile `YATSSMC` for `arduino:esp32:nano_nora` in CI so firmware regressions
  are caught on each push.

## Possible ESP32-C6 support

- If ESP32-C6 support is adopted, separate the controller pin assignments into
  board-specific configurations. The current Nano ESP32 sensor and track-power
  pin arrays are already centralized and do not need restructuring beforehand.
