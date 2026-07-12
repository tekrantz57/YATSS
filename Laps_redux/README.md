# Laps Redux Controller Sketch

This Arduino Nano ESP32 sketch is the microcontroller side of the lap timer. It
does not count laps. It timestamps debounced sensor edges and sends them to the
Windows app over serial.

## Board

The sketch is currently intended for the Arduino Nano ESP32:

```powershell
arduino-cli compile --fqbn arduino:esp32:nano_nora Laps_redux
```

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

## Dead Strip Circuit Notes

The working test circuit used:

- DB107 bridge rectifier on the two dead-strip braid wires
- H11L1 optocoupler / Schmitt trigger
- Pullup on the H11L1 output to ESP32 3.3V
- H11L1 output into the configured sensor pin
- Optional 0.1 uF capacitor across H11L1 VCC/GND
- Short, twisted pair from dead strip to circuit where practical

See `..\PROTOCOL.md` for frame formats and commands.
