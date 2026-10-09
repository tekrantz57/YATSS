# Normally open track-power relays

Standard wiring as of October 9, 2026, starting with firmware
`0.20.0-beta.2-no-relay.1`, for Nano ESP32, C5, C6, and UNO Q.
No installations exist yet. This is the standard setup.
Wiring and firmware must agree; relay polarity is not an app setting.
No lane GPIO mappings or Windows protocol commands change.

## Wiring one lane

The photographed 12 V five-pin automotive relay provides:

| Terminal | Connection |
| --- | --- |
| 30 (common) | Fused lane supply positive |
| 87 (normally open) | Driver station / lane positive feed |
| 87a (normally closed) | Unused; insulate |
| 86 (coil) | +12 V accessory/control supply |
| 85 (coil) | IRLZ44N drain |

Keep the MOSFET source at control ground, and connect MCU ground and control
supply negative to that ground. The MCU GPIO drives only the gate, never the
12 V coil. Retain the coil flyback diode: cathode/banded end to 86/+12 V,
anode to 85/drain. Verify markings and polarity for each actual relay;
relays with internal diodes are polarity-sensitive.

Add or verify an **external 10 kOhm gate-to-source pulldown per lane**, placed
near the MOSFET. This is a proposed hardware requirement, not something firmware
can add. It prevents an otherwise floating gate when the MCU is disconnected
or its output is high-impedance. Retain any existing series gate resistor.
Check that the selected GPIO is not driven high by boot/strapping circuitry;
a pulldown cannot override an actively driven boot signal. Scope/test reset
and power sequencing on each actual board and lane.

The previous IRLZ44N cell was bench-tested at relay-coil current. Its
[manufacturer specifications](https://www.infineon.com/part/IRLZ44N) give
on-resistance ratings at gate voltages above 3.3 V, not a guaranteed 3.3 V
rating. Verify coil pickup, MOSFET voltage drop, and temperature with the actual
3.3 V controller, especially during prolonged energized operation. For a new
driver design, prefer a part with an explicit suitable low-voltage gate rating.

The photographed relay is marked 30/40 A at 14 VDC. That is not a blanket
approval for higher track voltage or motor startup/stall current. Confirm the
manufacturer's DC motor-load switching rating, wiring/fuses, socket rating,
coil continuous-duty suitability, and temperature at the venue's supply.
Do not switch mains wiring with this design.

## Firmware and failure behavior

The shared core maps an enabled lane to a HIGH output; disabled lanes are LOW.
Both adapters preload LOW before configuring outputs, before transport startup.
Boot, RESET, OFF, watchdog trips, and cut-only diagnostic pulses de-energize
the affected coils. Commands/masks still describe actual track power, not coil
polarity. Diagnostic pulses now release the relay rather than energizing it.

Coil-supply loss opens healthy normally open contacts. Controller power loss
should also leave them open with the pulldown and validated GPIO behavior.
Restoring supplies does not grant permission to run: controller boot/recovery
must hold power off until an explicit verified host power command.

This improves the power-loss default but is not a certified safety circuit.
Welded contacts or a shorted MOSFET can leave power on. Retain an independent
emergency cutoff/interlock as appropriate. Flyback suppression can delay relay
release; measure actual power-cut latency rather than assuming instant cutoff.

## Setup and bench checks

1. Disconnect track and coil supplies. Never change live relay wiring.
2. Connect each lane supply to 30 and its lane feed to 87; insulate 87a.
3. Add/verify each pulldown and diode; verify common grounds and relay pin numbers.
4. Flash the matching normally open ESP32 firmware, or build/deploy the UNO Q
   App Lab sketch. Verify firmware and wiring agree before applying track power.
5. With track supply disconnected, test all lane masks and diagnostics. Check
   LOW means released coil and HIGH means energized coil. Check 30-87 continuity
   using an appropriate meter only on an unpowered contact circuit.
6. Test boot, MCU reset, unplugging the MCU, coil-supply removal/restoration,
   communication loss/watchdog, and independent emergency cutoff. No unintended
   coil pickup or automatic track restart should occur.
7. Only then test fused track power with a safe load, then actual cars. Measure
   release latency and check temperatures during a long enabled session.

Automated native tests exercise all 256 power masks and verify the shared
output rule during boot, watchdog, reset, and diagnostic operations. They do
not validate actual wiring, contact ratings, or MCU reset electrical behavior.

October 9 build checks: Nano ESP32, C5 N16R8, C6 N4/N8, and UNO Q compiled
successfully; the four ESP32 firmware packages and UNO Q App Lab import zip
were rebuilt. The native C++ test run was blocked by the local Visual Studio
compiler setup (launcher cannot find `cl`, and the inspected toolset lacks
headers). Run those tests in CI or after repairing the toolchain. No flashing
or physical relay validation was performed by these build checks.

The earlier standalone click-test sketch is not present in this repository.
If recovered, it can check the coil driver with track power disconnected, but
the click does not establish contact behavior, startup defaults, or cut latency.
