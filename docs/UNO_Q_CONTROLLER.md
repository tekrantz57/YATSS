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

## Onboard Status Matrix

The UNO Q's blue-only 8x13 LED matrix provides a static controller-health
indicator:

- A plain `Y` means the controller firmware is running and waiting for YATSS.
- A boxed `Y` means YATSS commands and keepalives are being received.
- `!` means the Windows keepalive watchdog expired or the sensor-transition
  queue overflowed.

The display uses one-bit brightness and is redrawn only when its state changes.
There are no animations or delays, and display calls never occur in a sensor
interrupt. A new valid YATSS command clears a communication warning. A queue
overflow remains latched until a controller reset so it cannot pass unnoticed.

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

## App Lab Runtime Lifecycle

App Lab is the editor and deployment control surface; its window is not the
runtime. Clicking **Run** performs these operations:

1. Compiles and flashes `sketch/sketch.ino` onto the STM32U585.
2. Provisions `python/main.py` in a Docker container on the UNO Q Linux system.
3. Starts that container in detached mode.
4. Publishes container TCP port 45991 on the Linux host.
5. Connects the Python process and STM32 sketch through Arduino RouterBridge.

In the UNO Q configuration tested on August 7, 2026, closing the App Lab window
caused TCP port 45991 to disappear even when **Stop** was not clicked. App Lab
can own forwarding for application ports and closes its port tunnels during
shutdown. Therefore, keep App Lab running whenever YATSS uses the UNO Q
controller. A detached Python container alone is not sufficient evidence that
the host-side TCP endpoint will remain available.

The TCP interface remains available only while the App Lab Python container is
running. It stops when any of the following occurs:

- **Stop** is clicked in App Lab.
- The App Lab window is closed.
- Another App Lab app replaces the running app.
- `python/main.py` or its container exits.
- Docker is stopped.
- The UNO Q is shut down, loses power, or reboots.

The STM32 sketch remains flashed after App Lab closes, but it cannot provide
TCP port 45991 by itself. The Python container owns TCP while RouterBridge
transports frames between Python and the STM32.

The TCP listener accepts a new YATSS connection in place of an older one.
Socket replacement is synchronized, and cleanup is tied to the specific failed
socket so a stale heartbeat callback cannot accidentally close a newly
accepted connection. Repeated stop/restart reconnect testing remains part of
the UNO Q bench checklist.

The current App Lab-generated container does not declare an automatic restart
policy. After an UNO Q reboot, open App Lab and start the app again. A
production standalone installation should eventually use a Linux `systemd`
service to start the installed app with `arduino-app-cli` and expose TCP port
45991 independently of the App Lab window after Linux and Docker are ready.
Until that service is implemented and tested, App Lab must remain open and the
controller TCP interface must not be expected to return automatically.

## Status

This first hardware-test implementation preserves YATSS protocol v4, including
lane edges, track-power masks, debounce configuration, diagnostics, keepalives,
and the power-cut watchdog. Each active-low sensor input uses a GPIO `CHANGE`
interrupt. The interrupt callback records only the lane, level, and MCU
timestamp in a fixed 64-entry queue. The normal MCU loop drains that queue,
applies debounce and sequence handling, and sends frames through RouterBridge;
no Bridge, string, or logging work occurs inside an interrupt callback. Bridge
latency therefore does not alter the recorded edge timestamp.

If the loop cannot drain transitions quickly enough, the controller reports
`ERR:QUEUE_FULL:<count>` and includes its cumulative dropped-transition count
in Controller Diagnostics. Entering or leaving diagnostics and resetting the
controller flushes queued transitions so an event cannot cross operating-mode
boundaries.

The sketch compiles with Arduino Zephyr core 0.90.0 and Arduino_RouterBridge
0.4.3 for `arduino:zephyr:unoq`. App Lab deployment and the Bridge/TCP transport
have completed an overnight demo stream with approximately 9,500 to more than
13,000 laps per lane and no disconnects, missed-frame growth, memory problems,
or stalled counting. On August 7, 2026, the lane 1 active-low input on D2 was
validated with a physical sensor through the complete ISR, RouterBridge/TCP,
and YATSS lap-processing path. Before track installation, validate lanes 2-8
and all outputs in Controller Diagnostics and bench-test the watchdog and relay
polarity. Include high-speed sensor testing while the status matrix is enabled
to confirm that its refresh interrupt does not affect edge capture.
