# Controller Firmware Updates

YATSS can provision a blank ESP32-C6-DevKitC-1 N4/N8 and update either an
existing C6 or Arduino Nano ESP32 from `File > Update Controller Firmware...`.

## Operator Procedure

1. Connect the C6 through its USB-C socket labeled `UART`, or connect the Nano
   through USB, and select its COM port in Configure.
2. Disconnect track power and relay-coil power. Leave only USB connected.
3. Return YATSS to Practice mode, stop simulated lap input, and close Controller
   Diagnostics.
4. Choose `File > Update Controller Firmware...` and verify the board shown in
   the warning.
5. Keep USB connected and YATSS open until verification completes.

For a running protocol-v4 controller, YATSS selects the matching package from
its reported board profile and runtime flash capacity. Older C6 firmware or a
blank C6 is read-only probed with `esptool` to distinguish N4 from N8. Other
legacy, recovery-mode, or blank devices cannot identify themselves, so YATSS
presents the bundled board families and requires the operator to confirm the
printed model. The C6 uploader probes the chip and capacity again immediately
before writing and refuses a different ESP32 family or package-capacity
mismatch. The Nano uploader targets Arduino USB VID `2341` and PID `0070`
through DFU.

YATSS requests `TRACK_POWER:OFF`, closes its serial connection, runs the board's
uploader, reconnects, and waits for the expected board profile and firmware
version. C6 receives a complete merged 4 MB or 8 MB image at flash offset `0x0`;
Nano receives the application image through its Arduino DFU interface. A
successful write that does not produce the expected identity is reported as
unverified.

## Uploader Acquisition

YATSS does not redistribute either uploader. For C6, it searches in this order:

1. `YATSS_ESPTOOL_PATH`, for development or managed installations.
2. A previously approved YATSS cache under
   `%LOCALAPPDATA%\YATSS\Tools\esptool\<version>`.
3. Espressif's uploader installed with the Arduino ESP32 core.

If none is found, YATSS asks before downloading the official Windows AMD64
archive directly from the Espressif `esptool` GitHub release. The URL, size, and
SHA-256 are pinned in source. The archive is verified before its executable is
installed in the per-user cache. Download failure or hash mismatch occurs
before YATSS releases the serial port and cannot alter the controller.

`esptool` is a separate Espressif project distributed under GPL-2.0-or-later.
Its license and source are available from
<https://github.com/espressif/esptool>. YATSS itself remains MIT licensed.

For Nano, YATSS searches `YATSS_DFU_UTIL_PATH`, its per-user cache, and
Arduino's installed tools. If necessary, it asks before downloading Arduino's
official `dfu-util 0.11.0-arduino5` archive from `downloads.arduino.cc`, verifies
Arduino's published SHA-256, and extracts it to the per-user cache. `dfu-util`
is GPL-2.0-or-later; source and license information are available from
<https://dfu-util.sourceforge.net/>.

## Firmware Package

The Windows project includes the C6/N4, C6/N8, and Nano files from
`YATSSMC\dist\*.yatssfw` in both build and publish output under `Firmware`. A
`.yatssfw` file is a ZIP containing:

- `manifest.json`, with package format, product, firmware version, board
  profile, chip, uploader backend, Arduino FQBN/core version, image name, flash
  offset, flash capacity, byte count, SHA-256, and any backend-specific USB
  identity.
- One `.bin` firmware image: merged full flash for C6 or application-only for
  Nano DFU.

Package format 2 adds required flash-capacity metadata and backend-specific USB
identity for safely selecting between the C6 and Nano upload paths.

YATSS validates package structure, board/chip/backend, image size, filename,
flash offset, and SHA-256 before presenting the update confirmation.

Build both packages from the repository root after changing the sketch or
firmware version:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Build-ControllerFirmware.ps1
```

The script requires Arduino CLI, the Espressif ESP32 core, and Arduino ESP32
Boards. It builds both C6 merged images and the Nano application image, then
replaces all three versioned packages in `YATSSMC\dist`. Commit the packages
with the source that produced them.

## Failure Recovery

If probing or writing fails, YATSS reopens its normal serial loop and leaves
track power disabled in application state. Keep relay-coil and track power
disconnected, check the selected COM port and USB cable, then retry. Espressif
bootloader mode is in ROM, so a blank C6 or a C6 with an interrupted application
flash can normally be programmed again. A Nano with its factory recovery
partition intact can enter DFU recovery by double-tapping RESET and be updated
again. If that recovery partition was erased, restore it with Arduino tooling
and esptool before using YATSS's application-only DFU updater. Do not rely on
software power control while firmware is absent or being replaced.
