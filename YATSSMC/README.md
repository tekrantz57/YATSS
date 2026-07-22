# YATSS Microcontroller Sketch

This Arduino Nano ESP32 sketch is the microcontroller side of the lap timer. It
does not count laps. It timestamps debounced sensor edges and sends them to the
Windows app over serial.

## Board

The sketch is currently intended for the Arduino Nano ESP32:

```powershell
arduino-cli compile --fqbn arduino:esp32:nano_nora YATSSMC
```

To upload from Arduino IDE, select the Arduino Nano ESP32 board and upload the
`YATSSMC` sketch folder. If `dfu-util` fails after a successful compile, see
`..\docs\TROUBLESHOOTING.md`.

## Sensor Inputs

Sensor inputs use `INPUT_PULLUP` and `FALLING` interrupts. The expected signal
is normally high and pulled low when the dead-strip/opto circuit trips.

Current logical lane to physical input map:

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

Current logical lane to track-power output map:

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

### Relay Driver Notes

The bench-tested relay driver cell used:

- ESP32 GPIO into an IRLZ44N MOSFET gate
- Shared ground between the ESP32/control circuit and relay supply
- MOSFET low-side switching for the 12V relay coil
- Flyback diode across the relay coil

In this arrangement the ESP32 pin only drives the MOSFET gate. The MOSFET sinks
the relay-coil current, so the GPIO does not carry the coil load.

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
