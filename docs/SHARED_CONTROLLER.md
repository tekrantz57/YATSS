# Shared controller core

All supported controllers use `Controller/YatssController.h`, with firmware
identity defined once in `Controller/FirmwareVersion.h`. The header uses fixed
storage and standard C++11-compatible types; it does not depend on Arduino,
ESP-IDF, Zephyr, RouterBridge, or the Windows application.

The separate .NET `YATSS.Protocol` project links the app's canonical
`LapProtocol.cs` parser/checksum code for both the app and portable
[TCP simulator](TCP_CONTROLLER_SIMULATOR.md). That simulator tests app transport
and recovery; it does not replace the native C++ shared-core tests or emulate
sensor ISR execution.

## Ownership

The core owns protocol-v5 framing/checksums, timing-session identity, command parsing, per-lane debounce,
sequences, bounded edge queues, heartbeat deadlines, diagnostics, relay-pulse
lifecycle, and the five-second host-command watchdog. It never counts laps.

`YATSSMC/YATSSMC.ino` is the ESP32 adapter: Nano/C5/C6 pin profiles, active-low
inputs and relay polarity, ESP32 critical sections, ISR placement in IRAM,
serial transport, runtime flash capacity, and hardware restart.
It obtains session randomness from the ESP32 hardware RNG, temporarily enabling
its entropy source while RF and ADC subsystems are unused.

`YATSSUnoQ/sketch/sketch.ino` is the UNO Q adapter: D2-D9 sensor inputs, existing
relay outputs, Zephyr interrupt locking, RouterBridge transport, MCU flash
identity, and the existing static status matrix. Python/TCP handling is unchanged.
It obtains randomness from Zephyr `sys_csrand_get`; the pinned core enables the
STM32 hardware entropy device and hardware-backed generator. Entropy failure
leaves power off, suppresses sensor capture, and refuses energizing commands.
Session generation runs in foreground code, never in a sensor ISR.

The adapter APIs are documented by
[Espressif's RNG guide](https://docs.espressif.com/projects/esp-idf/en/stable/esp32c6/api-reference/system/random.html)
and [Zephyr's random API](https://docs.zephyrproject.org/latest/doxygen/html/group__random__api.html).
The installed UNO Q core's configuration also enables the STM32 entropy driver
and hardware-backed CSPRNG; compilation verifies the API is available to sketches.

Every controller response carries its session, including acknowledgements and
diagnostics. Session rotation does not change pins or timestamp capture. See
[serial protocol](SERIAL_PROTOCOL.md) and [recovery](CONTROLLER_RECOVERY.md).

Both adapters timestamp in their sensor ISR. The core captures and debounces
edges and assigns sequences inside that protected ISR call. No allocation,
frame formatting, serial/Bridge calls, or display updates occur there. The
foreground loop drains accepted edges and formats/sends frames. ESP32 uses a
32-slot queue and UNO Q uses 64; each ring retains one empty slot. Each service
call drains at most one queue's worth of edges to avoid starving safety checks.

ESP32 keeps falling-edge capture during normal operation and switches to CHANGE
for diagnostics. A captured falling edge counts even if the pulse has ended by
the time the ISR reads GPIO. UNO Q keeps CHANGE capture and ignores clear edges
outside diagnostics. Pin maps and relay polarity have not changed.

## Unified behavior and intentional differences

- Diagnostics counts all transitions in the ISR but coalesces transport updates
  to the latest changed state per lane. This was the ESP32 behavior and now
  applies to UNO Q. Counts are retained even if several transitions occur between
  display updates; diagnostics are not a full raw-transition archive.
- Entering/leaving diagnostics flushes pending race edges and clears timing
  baselines, so events cannot cross mode boundaries. A timestamp of zero is a
  valid debounce baseline. Unsigned 32-bit arithmetic handles clock wrap.
- Power commands cancel pending diagnostic relay-pulse restoration on both
  platforms. A watchdog trip also cancels restoration before writing all lanes
  off, even if the loop services an overdue pulse and watchdog simultaneously.
  UNO Q previously did not consistently cancel those pending restores.
- RESET cuts all lanes, clears edge queues/sequences and warnings, and stops
  diagnostics. ESP32 then actually reboots; UNO Q resets session state while
  retaining uptime and RouterBridge. Both emit `HELLO:RESETTING`. UNO Q retains
  its configured debounce, as before; ESP32 reboot restores the default.
- Valid checksummed host commands arm/refresh the watchdog. Invalid checksums
  do not. Ordinary KEEPALIVE does not extend the separate diagnostic timeout.
- Relay pulses remain cut-only tests capped at two seconds. Both adapters share
  the active-high run mapping for normally open contacts; OFF releases coils.
  See [Relay wiring](RELAY_WIRING.md). A stalled
  transport/foreground loop still requires hardware validation; source reuse
  does not create an independent hardwired safety interlock.

## IDE, CLI, App Lab, and packaging

Arduino builders stage sketches before compiling, and App Lab imports an app
directory. To keep both entry points self-contained, each sketch contains a
generated `src/YatssController` copy. These checked-in files are build inputs,
not additional maintained implementations. Do not edit them directly.

After editing the canonical core or version, synchronize from the repository:

```powershell
./tools/Sync-ControllerCore.ps1
./tools/Sync-ControllerCore.ps1 -Check
```

```bash
bash tools/sync-controller-core-linux.sh
bash tools/sync-controller-core-linux.sh "$PWD" check
```

Open `YATSSMC/YATSSMC.ino` in Arduino IDE as before, or compile either sketch
with its existing FQBN. No additional Arduino library install or absolute-path
include is needed. App Lab ZIPs and Windows publish folders include the UNO Q
core copy under `sketch/src`, so the imported app does not need this repository.

Firmware/App Lab build scripts and CI refuse stale copies. Windows publishes
also verify the copies through MSBuild. Release firmware images must be rebuilt
with `tools/Build-ControllerFirmware.ps1` after a core/version change; existing
precompiled `.yatssfw` files do not update just because source files changed.
MSBuild also refuses publishing if the required firmware packages do not match
the canonical source version, rather than silently bundling an older image.
No board is flashed by synchronization, tests, builds, or ZIP creation.

## Verification

Native tests in `tests/controller/main.cpp` exercise the real header for both
queue sizes: checksums, commands, all lanes, timestamp-zero debounce, bounds,
queue overflow, diagnostics and mode isolation, relay cancellation, watchdog
expiry, reset, and clock wrap. Transport output is checked to be outside critical
sections. Run on Windows with Visual Studio's C++ build tools:

```powershell
./tools/Test-ControllerCore.ps1
# Or explicitly select an existing GCC-compatible compiler:
./tools/Test-ControllerCore.ps1 -Compiler C:/path/to/g++.exe
```

The Windows launcher selects a Visual Studio installation that actually has
the MSVC C++ tools registered, not simply the newest IDE. If none is available,
use Visual Studio Installer > Modify > Desktop development with C++, with the
MSVC x64/x86 tools and a Windows SDK selected. Compiler executables without the
standard-library headers and SDK are not a working installation. The launcher
reports that condition before attempting compilation.

```bash
bash tools/test-controller-core-linux.sh
```

CI checks generated copies, runs native tests with both GCC on Linux and MSVC
on Windows, and compiles Nano ESP32, C5
N16R8, C6 N4/N8, and UNO Q against pinned board-core versions. A compile is not
a hardware test. Bench-test all sensors/outputs, high-speed capture,
diagnostic transitions, resets, watchdog trips, and repeated reconnects on the
refactored firmware before venue use. Earlier UNO Q sensor/overnight results
describe the previous firmware, not a completed bench test of this refactor.

### October 10, 2026 verification

GitHub [run 37996620059](https://github.com/tekrantz57/YATSS/actions/runs/37996620059)
passed all seven jobs for commit `291ceb5`: Windows solution/tests, Linux native
controller behavior, generated-source checks, and all five firmware builds.
The latest local Windows solution and test suite also pass, including the new
second-based heat durations. Those uncommitted changes have not run on GitHub.

The added Windows/MSVC native-test step still needs its first GitHub run after
push. The local Visual Studio C++ setup was repaired by adding MSVC x64/x86
tools and Windows SDK 10.0.26100. The native launcher now compiles cleanly with
`/W4 /WX` and passes all 6,328 checks locally, including normally open relay
mapping for every lane-power mask. C++ tools are a development/test requirement,
not a requirement for users running the Windows application.
The installer returned success-with-restart-required (3010); no restart was
performed. Native tests passed before restarting, but Windows should be
restarted when convenient to finish the installation.
