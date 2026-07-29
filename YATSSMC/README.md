# YATSS Microcontroller Sketch

This ESP32 sketch is the microcontroller side of the lap timer. It does not
count laps. It timestamps debounced sensor edges and sends them to the Windows
app over serial.

## Board

The sketch supports both controller boards without requiring source edits. The
selected Arduino board determines the pin map at compile time.

### ESP32-C6-DevKitC-1 V1.2

The production candidate is an Espressif ESP32-C6-DevKitC-1 V1.2 with an
ESP32-C6-WROOM-1-N8 module. In Arduino IDE:

1. Install `esp32 by Espressif Systems` in Boards Manager.
2. Select `ESP32C6 Dev Module` as the board.
3. Select the COM port created by the Silicon Labs CP210x interface.
4. Connect and upload through the USB-C socket labeled `UART`.
5. Set `Flash Size` to `8MB (64Mb)`.
6. Set `Partition Scheme` to `8M with spiffs (3MB APP/1.5MB SPIFFS)`.
7. Leave `USB CDC On Boot` disabled.

The equivalent CLI build is:

```powershell
arduino-cli compile `
  --fqbn "esp32:esp32:esp32c6:CDCOnBoot=default,FlashSize=8M,PartitionScheme=default_8MB" `
  YATSSMC
```

To build the validated C6 and Nano firmware packages embedded in Windows
publish output:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Build-ControllerFirmware.ps1
```

This compiles a merged C6/N8 flash image plus a Nano application image and
writes two `.yatssfw` packages under `YATSSMC\dist`. The packages contain YATSS
firmware only; the required Espressif or Arduino uploader is located or
downloaded by the Windows app when an update is requested.

The C6 profile preserves GPIO16/GPIO17 for the CP2102N USB-to-UART bridge and
avoids the board's boot-strapping pins and GPIO8 RGB LED. GPIO12 and GPIO13 are
repurposed from native USB for lanes 8 and 7 track-power outputs. Do not connect
the USB-C socket labeled `USB` while using this mapping; use only the socket
labeled `UART`.

### Arduino Nano ESP32

The original Arduino Nano ESP32 profile remains supported:

```powershell
arduino-cli compile --fqbn arduino:esp32:nano_nora YATSSMC
```

To upload from Arduino IDE, select `Arduino Nano ESP32` and upload the `YATSSMC`
sketch folder. If `dfu-util` fails after a successful compile, see
`..\docs\TROUBLESHOOTING.md`.

## Sensor Inputs

Sensor inputs use `INPUT_PULLUP` and `FALLING` interrupts. The expected signal
is normally high and pulled low when the dead-strip/opto circuit trips.

ESP32-C6 logical lane to physical input map:

| Windows lane | Protocol lane | ESP32-C6 GPIO |
| --- | ---: | ---: |
| 1 | 0 | 0 |
| 2 | 1 | 1 |
| 3 | 2 | 2 |
| 4 | 3 | 3 |
| 5 | 4 | 6 |
| 6 | 5 | 7 |
| 7 | 6 | 10 |
| 8 | 7 | 11 |

Arduino Nano ESP32 logical lane to physical input map:

| Windows lane | Protocol lane | ESP32 pin |
| --- | ---: | --- |
| 1 | 0 | D2 |
| 2 | 1 | A4 |
| 3 | 2 | D4 |
| 4 | 3 | D5 |
| 5 | 4 | D6 |
| 6 | 5 | D7 |
| 7 | 6 | D8 |
| 8 | 7 | D9 |

Lane 2 is intentionally mapped to `A4` instead of `D3` because `D3` did not
generate reliable interrupt edges on the bench-tested board. The Windows app
still sees it as protocol lane `1`.

## Track Power Outputs

ESP32-C6 logical lane to track-power output map:

| Windows lane | ESP32-C6 GPIO |
| --- | ---: |
| 1 | 23 |
| 2 | 22 |
| 3 | 21 |
| 4 | 20 |
| 5 | 19 |
| 6 | 18 |
| 7 | 13 |
| 8 | 12 |

Arduino Nano ESP32 logical lane to track-power output map:

| Windows lane | ESP32 pin |
| --- | --- |
| 1 | D10 |
| 2 | D11 |
| 3 | D12 |
| 4 | D13 |
| 5 | A0 |
| 6 | A1 |
| 7 | A2 |
| 8 | A3 |

`TRACK_POWER_CUT_ACTIVE_LEVEL` is currently `HIGH`. That means the sketch writes
the active level to cut power and the opposite level to restore power.

The sketch drives every track-power GPIO to the cut level before serial startup
and its one-second boot delay. After Windows connects, valid commands arm a
five-second watchdog. Windows acknowledges each controller heartbeat; if those
acknowledgements stop while track power is enabled, the controller cuts all
lanes and requires another explicit track-power command before power can be
restored.

This watchdog protects against loss of Windows communication while the
controller remains powered. It cannot keep the track off if controller or
relay-coil power is lost: the normally closed contacts documented below return
to their unpowered state, which supplies track power. Use normally open safety
contacts or an independent interlock where power loss must fail to off.

### Relay Driver Notes

The bench-tested relay driver cell used:

- ESP32 GPIO into an IRLZ44N MOSFET gate
- Shared ground between the ESP32/control circuit and relay supply
- MOSFET low-side switching for the 12V relay coil
- Flyback diode across the relay coil

In this arrangement the ESP32 pin only drives the MOSFET gate. The MOSFET sinks
the relay-coil current, so the GPIO does not carry the coil load.

One lane of the relay driver and track-power cutoff wiring:

```text
Control / relay-coil side

ESP32 track-power GPIO  -------------------- IRLZ44N gate

12V relay/control +  ----+------------------ relay coil +
                         |
                         +----|<|----+
                              diode  |
                                     |
IRLZ44N drain      ------------------+------ relay coil -
IRLZ44N source     ------------------------- control GND
ESP32 GND          ------------------------- control GND
12V relay/control - ------------------------- control GND

Diode cathode/banded end goes to relay/control +.
Diode anode goes to the MOSFET drain / relay coil - side.


Track-power contact side, one lane

lane power supply +  ----------------------- relay COM
relay NC          -------------------------- driver station / lane feed +
lane power supply -  ----------------------- driver station / lane feed -
relay NO          -------------------------- unused with current active-high cut
```

With `TRACK_POWER_CUT_ACTIVE_LEVEL` set to `HIGH`, the relay energizes when
YATSS cuts track power. Using `COM` and `NC` means the lane feed is connected
when the relay is relaxed, and opened when the relay clicks.

For track-power cutoff, wire the lane supply through the relay contacts before
the driver station / lane feed. Use the relay contact side for the actual track
power path, and use the MOSFET driver side for the relay coil.

Prefer a separate 12V accessory/control supply for relay coils, or a shared 12V
control supply tapped before any lane-specific driver wiring. This keeps relay
coil current out of the racer lane supplies and avoids lane-to-lane voltage or
fairness questions. If each relay coil is powered from its own lane supply, it
will probably work with a strong regulated supply, but measure voltage with the
relay off, relay on, and car running at both the driver station and the track.

## Edge Handling

Each lane has a per-lane debounce interval. The default is:

```cpp
#define DEFAULT_EDGE_DEBOUNCE_MILLIS 1800UL
```

Windows can change this at runtime with `CONFIG:DEBOUNCE:<milliseconds>`. The
maximum accepted value is `10000`.

Edges are queued from interrupt handlers and published from the main loop as:

```text
EDGE:<zero-based-lane>:<per-lane-sequence>:<millis>*XX
```

The queue is protected with ESP32 critical-section APIs.

Serial command reads use a 10 ms timeout so an incomplete command cannot block
edge publishing or heartbeats for the Arduino default timeout.

## Controller Diagnostics

The Windows app can start a diagnostic session from
`File > Controller Diagnostics` while in Practice mode. During the session,
sensor interrupts are
temporarily changed from `FALLING` to `CHANGE`, normal `EDGE` frames are
suppressed, and raw sensor state changes are reported with per-lane transition
and debounced accepted-edge counts. Closing diagnostics restores the normal
falling-edge interrupt mode and clears the debounce baselines.

Relay tests are cut-only pulses with a maximum duration of two seconds. Pulse
timing and restoration are owned by the controller, so the previous power mask
is restored even if Windows stops responding during a pulse. A later explicit
track-power command cancels the pending restoration. Diagnostic sessions time
out after five seconds without a diagnostic command; the Windows window sends a
status request once per second while open.

## Dead Strip Circuit Notes

The working test circuit used:

- DB107 bridge rectifier on the two dead-strip braid wires
- H11L1 optocoupler / Schmitt trigger
- 470 ohm current-limit resistor from DB107 `+` to H11L1 pin 1
- 7.5 kOhm pullup from H11L1 pin 4 to ESP32 3.3V
- H11L1 output into the configured sensor pin
- Optional 0.1 uF capacitor across H11L1 VCC/GND
- Short, twisted pair from dead strip to circuit where practical

One lane of the current circuit:

```text
                         isolated dead strip
                         braid A      braid B
                           |            |
                           |            |
                         DB107 bridge rectifier
                         ~              ~
                         +              -
                         |              |
                       470 ohm          |
                         |              |
                         |              |
H11L1 input side      pin 1          pin 2
                      anode        cathode


ESP32 3.3V  --------------------+---------------- H11L1 pin 6 VCC
                                |
                              7.5 kOhm
                                |
sensor GPIO  -------------------+---------------- H11L1 pin 4 OUT

ESP32 GND   ------------------------------------- H11L1 pin 5 GND

Optional: 0.1 uF / 104 capacitor between H11L1 pin 6 VCC and pin 5 GND,
placed close to the H11L1.
```

H11L1 pinout, viewed from the top with the notch/dot end at pin 1:

```text
        H11L1
      +-------+
  1 --|       |-- 6  VCC
  2 --|       |-- 5  GND
  3 --|       |-- 4  OUT
      +-------+
```

The DB107 bridge makes the dead-strip input polarity-independent. The H11L1
output is normally pulled high to 3.3V; when a car bridges the dead strip and
the optocoupler trips, the output pulls the ESP32 sensor pin low. The sketch is
therefore configured for `INPUT_PULLUP` and `FALLING` interrupts.

See `..\docs\SERIAL_PROTOCOL.md` for frame formats and commands.
