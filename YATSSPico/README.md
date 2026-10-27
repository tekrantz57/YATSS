# YATSS Pico 2 W Controller

Experimental Raspberry Pi Pico 2 W (RP2350, 4 MB flash) adapter, using exactly
the same `Controller/YatssController.h` and firmware version as ESP32 and UNO Q.
Only GPIO, ISR attachment, critical sections, USB serial, session entropy,
flash identification and reset are board-specific. It never counts laps.

Protocol, checksums, boot sessions, debounce, queues, diagnostics and the
five-second communication watchdog remain in the shared core. The adapter uses
a 32-slot queue, like ESP32, and timestamps in the sensor ISR. It uses only core
0; no Wi-Fi/Bluetooth application or second-core task is started. Normal capture
uses FALLING; diagnostics use CHANGE. SDK `get_rand_64()` supplies session IDs
in foreground code; these are stale-frame identifiers, not authentication keys.
RESET cuts outputs through the shared core and then reboots the RP2350.

## Arduino IDE and CLI

Use Earle Philhower's **Raspberry Pi Pico/RP2040/RP2350** Arduino core, pinned
to **6.3.1** for this adapter. This is not Arduino's Mbed RP2040 package.
Add this Additional Boards Manager URL in IDE preferences:

```text
https://github.com/earlephilhower/arduino-pico/releases/download/global/package_rp2040_index.json
```

Install that core and select **Raspberry Pi Pico 2W**. Open
`YATSSPico.ino`, retain default ARM architecture and USB stack/settings, and
compile/export the binary. Select the COM port after the first UF2 upload.
The sketch contains a generated shared-core copy, so IDE builds need no manual
library installation. Do not edit `src/YatssController`; synchronize from the
canonical source with `tools/Sync-ControllerCore.ps1`.

CLI installation and compile-only build:

```powershell
arduino-cli core update-index --additional-urls https://github.com/earlephilhower/arduino-pico/releases/download/global/package_rp2040_index.json
arduino-cli core install rp2040:rp2040@6.3.1 --additional-urls https://github.com/earlephilhower/arduino-pico/releases/download/global/package_rp2040_index.json
./tools/Build-PicoFirmware.ps1
```

The build script checks synchronization and the installed core version, writes
`artifacts/pico2w/YATSSPico.ino.uf2`, and prints its SHA-256. It does not flash.
The equivalent compile is:

```powershell
arduino-cli compile --fqbn rp2040:rp2040:rpipico2w --output-dir artifacts/pico2w YATSSPico
```

## First Upload and Connection

Leave sensors, relay drivers and track power disconnected for initial testing.
Hold BOOTSEL while connecting a micro-USB data cable. Check the drive's
`INFO_UF2.TXT` identifies RP2350, then copy this board's UF2 onto that drive.
It reboots and the drive disappears; a USB COM port should appear instead.
Do not copy it to an unrelated drive or upload an ESP32 image.

Select the new COM port in YATSS at 115200 baud, with simulated input disabled.
The existing app accepts protocol-5 identities without a board-specific scoring
path. Expect profile `RASPBERRY_PI_PICO_2_W`, 4,194,304 flash bytes, a nonzero
session and heartbeats. The startup HELLO can be missed before the port opens;
YATSS requests identity during its normal handshake. No wait-for-serial loop
blocks controller startup. Closing a terminal releases the port for YATSS.

With YATSS/Serial Monitor closed, an optional twelve-second connection check is:

```powershell
./tools/Test-PicoConnection.ps1 -Port COM7
```

Replace COM7 with the actual Pico port. It checks profile/flash/session,
checksums, heartbeats and OFF acknowledgement. It sends only PING, KEEPALIVE
and power-OFF commands, never energizing commands. This is not a GPIO test.

In-app Pico firmware updates and bundled Pico firmware packages are **not**
implemented. Use BOOTSEL/UF2 or Arduino IDE/CLI for now. Original Pico/RP2040,
non-W Pico 2 and third-party RP2350 boards are not this adapter's declared target.

## Pin Map

GP numbers below are GPIO identifiers, not physical header pin numbers. Hold
the board component-side up, USB connector at the top; verify against the
[official Pico 2 W pinout](https://pip.raspberrypi.com/categories/1088-raspberry-pi-pico-2-w).

| Lane | Sensor GPIO | Physical pin | Relay-control GPIO | Physical pin |
| --- | --- | --- | --- | --- |
| 1 | GP0 | 1 | GP8 | 11 |
| 2 | GP1 | 2 | GP9 | 12 |
| 3 | GP2 | 4 | GP10 | 14 |
| 4 | GP3 | 5 | GP11 | 15 |
| 5 | GP4 | 6 | GP12 | 16 |
| 6 | GP5 | 7 | GP13 | 17 |
| 7 | GP6 | 9 | GP14 | 19 |
| 8 | GP7 | 10 | GP15 | 20 |

3V3(OUT) is physical pin 36; ground includes pins 3, 8, 13, 18 and 38.
Inputs are active-low with internal pull-ups. Use the documented dead-strip
conditioning and verify external biasing/valid logic levels, including RP2350
input errata, before physical testing. GPIO is 3.3 V, not 5 V tolerant.

Outputs are LOW/off at setup and HIGH only when the corresponding shared-core
power bit is enabled, for normally open relay contacts and external low-side
drivers. Fit the external driver pulldowns described in
[relay wiring](../docs/RELAY_WIRING.md); software cannot set pin levels while
the chip is unpowered or before setup. Never connect relay coils, track power
or unconditioned dead strips directly to GPIO.

## Validation Limits

October 25 development firmware adds controller-side capture masks. The
earlier first-board/lane-1 checks below predate that change. New interrupt
selection and restoration require physical verification; see
[Practice sensor arming](../docs/PRACTICE_SENSOR_ARMING.md).

October 24, 2026 first-board check: compiled with Arduino-Pico 6.3.1, flashed
through the RP2350 BOOTSEL drive, and enumerated on this Windows machine as
COM27. The connection test passed with 14 checksummed frames, 12 heartbeats,
the expected profile/flash capacity, a nonzero session and power-OFF
acknowledgement. No power-ON commands were sent. The shared controller's 6,722
native checks also passed. Firmware uses 316,592 flash bytes and 71,480 RAM
bytes in this build. Port numbers and build sizes can change.

Later October 24, the user verified a physical lane 1 sensor on GP0 (header
pin 1): serial traffic was received and laps counted correctly in YATSS.
This is an end-to-end lane 1 functional pass, not a calibrated timing or
all-lane stress test. Lanes 2-8 and relay/power-output operation remain untested.

Compilation and the common-core tests do not establish Pico electrical safety
or sensor reliability. Verify all eight lanes, short pulses, bounce/debounce,
simultaneous edges, queue overflow, diagnostics transitions, reconnects and
boot-session changes using the physical board. Verify off-state through boot,
BOOTSEL, resets, unplugging, watchdog expiry and control-power loss/restoration.
USB writes and the foreground loop are not an independent hardware interlock.
The second-MCU pulse fixture proposed in TODO can provide repeatable stimuli.

References: [Arduino-Pico installation](https://arduino-pico.readthedocs.io/en/latest/install.html),
[Pico SDK high-level APIs](https://www.raspberrypi.com/documentation/pico-sdk/high_level.html),
[shared controller design](../docs/SHARED_CONTROLLER.md).
