# YATSS C3 Bench Pulse Fixture

Standalone ESP32-C3 Super Mini generator; not YATSSMC and not for race use.
This adapts the MIT-licensed Drag `dragPulseTest` RMT plan and scheduler. The
single output uses GPIO4, active LOW at the Nano in DIRECT mode. Verify the
actual board and pin labels before connecting; some Mini layouts differ.
The copied Drag license is in `DRAG-LICENSE.txt`.
Build for `esp32:esp32:esp32c3:CDCOnBoot=cdc` with Espressif Arduino core 3.x.
Uploading replaces the C3's existing firmware. Do not flash the Nano.
The C3 RMT adapter uses a 48-symbol block; see [Espressif's RMT documentation](https://docs.espressif.com/projects/arduino-esp32/en/latest/api/rmt.html).

## Before connecting

Keep both boards unplugged and the track and relay power disconnected. Confirm
Nano lane 1 is D2. Remove the H11L1 pin 4 output connection from D2; do not
drive D2 while any sensor output, other driver, or higher-voltage pull-up is
connected. A resistor alone does not make output contention or 5 V safe. Check
the actual board and other attached wiring with a meter. Do not join the boards'
3.3 V or 5 V supplies. Confirm the 7.5 kOhm pull-up is to Nano 3.3 V, not 5 V.

Direct first:

```text
C3 GND ---------------- Nano GND
C3 GPIO4 --- 1 kOhm --- Nano D2 (lane 1 input)
                         (H11L1 output disconnected)
```

Connect only grounds first with both unplugged. Leave the signal wire unplugged.
Power each board from its own USB, flash/start the fixture, and verify C3 GPIO4
idle HIGH relative to common ground is about 3.3 V. Verify D2 is an input and
neither side exceeds 3.3 V. Set `MODE DIRECT`, `LEVEL 1`, and only then connect
the series-resistor signal. Start receiver capture after the line is stable;
connection and reset can themselves produce a transition. For a static meter
check, `LEVEL 0` pulls D2 LOW and `LEVEL 1` releases it HIGH. Disconnect signal
before either board powers down, resets, uploads, or changes mode. USB grounds
may already be connected through the computer; check for unexpected voltage
before connecting grounds. Neither board is 5 V tolerant by assumption.

## Optional NPN interface

With signal disconnected and both boards powered, select `MODE NPN`, then power
down before wiring. Verify the transistor's actual pinout; e.g. a suitable
2N3904 has manufacturer/package-dependent pin order:

```text
C3 GPIO4 --- 2.2k --- base   NPN
                       |      collector --- Nano D2
                      47k          |         (H11L1 output disconnected)
                       |          7.5k
common GND -------- emitter        |
                              Nano 3.3 V
```

The base pull-down connects base to common ground; emitter connects to common
ground. Use one pull-up, not a second parallel pull-up if one is already there.
Nano D2 is released HIGH when the C3 output is LOW and pulled LOW when it is
HIGH. Keep signal disconnected during resets: startup defaults to DIRECT and
could briefly drive the NPN on. Re-establish `MODE NPN` after every reboot,
check clear and blocked voltage with a meter, then connect and arm capture.
Never connect either board's supply to the other's supply.

## Commands

Serial monitor: 115200 baud, newline, uppercase. `HELP`, `LEVEL 0`, `LEVEL 1`,
`STOP`, `MODE DIRECT`, `MODE NPN`, or `RUN width_us gap_us count jitter_us`.
RUN requires clear level. Example slow run: `RUN 2000 3000000 20 1000`.
Example stress run: `RUN 1000 20000 1000 997`. Width 100..1000000 us, gap
100..60000000 us, count 1..1000000, jitter 0..1000000 us. The fixed-seed
jitter adds 0..jitter us to each gap. First pulse is delayed one second.
RMT is initialized during boot and retained between runs to avoid a pin
ownership change at RUN. This needs physical verification: an earlier build
produced an extra edge one second before the first requested pulse.
RMT programs pulse width; software schedules the gap, which can stretch.
The summary gives requested and completed pulses, requested gap range,
software-observed rearm range, and maximum scheduling lateness. These are not
measurements at the wire. A STOP during a pulse may cause an uncounted partial
pulse; measure electrical timing with an oscilloscope/logic analyzer.

YATSS controller debounce and app minimum lap time suppress pulses closer than
their configured values. For stress testing, use controller diagnostics and
record its debounce and counters; do not equate race laps with raw pulses.
Diagnostics itself changes interrupt mode and is not a race-mode validation.
Run the host-side timing-plan tests with `tools/Test-PulseFixture.ps1` on Windows,
or compile `tests/pulse-fixture/main.cpp` with a C++17 compiler elsewhere.

## Conditioning input side

An independently powered, isolated low-voltage source and an isolated switch
could apply controlled pulses to the bridge's two `~` inputs, keeping that side
separate from the Nano/C3 grounds. A separate isolated driver is required for
automated pulses; do not connect C3 GPIO or ground directly to the track/bridge
side. The 470 ohm input resistor, bridge diode drops, source voltage and H11L1
LED current determine whether it switches and stays within ratings. Confirm the
actual component datasheets, complete circuit (including the two diodes),
source limits and meter readings before designing or applying this stimulus.
No particular battery or voltage is approved here. This is not a substitute
for the real track, motor noise, or measured wire-level pulse timing.
