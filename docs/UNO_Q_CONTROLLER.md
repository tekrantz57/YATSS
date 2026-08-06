# Arduino UNO Q Integrated Controller

The `YATSSUnoQ` App Lab app is an experimental controller implementation for
the STM32U585 microcontroller built into the Arduino UNO Q. It allows the UNO Q
to run both YATSS under Wine and the real-time lane controller without a
separate ESP32.

The Arduino Router remains enabled. The MCU sketch exchanges protocol messages
with the Python side through Arduino Bridge RPC. Python publishes the YATSS
controller protocol on TCP port 45991. This avoids asking Wine to treat a Linux
pseudo-terminal as a physical serial device. A `yatss-unoq` pseudo-terminal is
also created in the App Lab app directory for command-line diagnostics.

## Pin Map

| Lane | Sensor input | Track-power cut output |
| ---: | --- | --- |
| 1 | D2 | D10 |
| 2 | D3 | D11 |
| 3 | D4 | D12 |
| 4 | D5 | D13 |
| 5 | D6 | A0 |
| 6 | D7 | A1 |
| 7 | D8 | A2 |
| 8 | D9 | A3 |

Sensor inputs use the internal pull-up and become active when pulled low. A
track-power cut output is active high. External sensor conditioning and relay
or solid-state switching hardware are still required; do not connect track
power directly to an UNO Q pin.

D0/D1 remain available for the hardware UART. A4/A5 remain available for I2C.

## Install And Run

Build an App Lab import archive on Windows with:

```powershell
.\tools\Build-UnoQAppPackage.ps1
```

Import `artifacts\YATSS-UNOQ-AppLab.zip` in App Lab, select the UNO Q, and run
the app. The packaging script deliberately writes portable `/` ZIP paths;
PowerShell's `Compress-Archive` writes Windows `\` paths that App Lab can
misreport as a missing `python/main.py`. App Lab installs the sketch on the MCU
and runs `python/main.py` on Debian. Keep `arduino-router` enabled.

After the app reports that TCP port 45991 is available, open YATSS configuration
and select:

```text
TCP:127.0.0.1:45991
```

No Wine COM-port mapping is needed. The serial log should report that the TCP
controller connection opened and then show the controller identity and
heartbeats.

For a Linux-side diagnostic independent of YATSS, locate and read the optional
pseudo-terminal:

```bash
ENDPOINT=$(find /home/arduino/ArduinoApps -maxdepth 2 -name yatss-unoq -print -quit)
timeout 10 cat "$ENDPOINT"
```

Select `COM6` in YATSS. The serial log should identify the controller as
`ARDUINO_UNO_Q_STM32U585` and then show one heartbeat per second.

## Status

This first hardware-test implementation preserves YATSS protocol v4, including
lane edges, track-power masks, debounce configuration, diagnostics, keepalives,
and the power-cut watchdog. Sensor edges are sampled and timestamped on the MCU;
Bridge latency does not alter the recorded timestamp.

The sketch compiles with Arduino Zephyr core 0.90.0 and Arduino_RouterBridge
0.4.3 for `arduino:zephyr:unoq`. Before track installation, validate all eight
inputs and outputs in Controller Diagnostics and perform a sustained demo test.
The App Lab deployment, Bridge pseudo-serial transport, and physical MCU pins
still require hardware validation on the UNO Q.
