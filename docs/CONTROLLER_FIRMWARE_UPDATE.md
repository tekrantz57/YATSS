# Controller Firmware Updates

YATSS can provision a blank ESP32-C6-DevKitC-1 or update an existing C6 from
`File > Update Controller Firmware...`. Arduino Nano ESP32 remains supported by
the sketch and package design, but its in-app upload backend is future work.

## Operator Procedure

1. Connect the C6 through the USB-C socket labeled `UART` and select its COM
   port in Configure.
2. Disconnect track power and relay-coil power. Leave only USB connected.
3. Return YATSS to Practice mode, stop simulated lap input, and close Controller
   Diagnostics.
4. Choose `File > Update Controller Firmware...` and verify the board shown in
   the warning.
5. Keep USB connected and YATSS open until verification completes.

For a running protocol-v3 controller, YATSS compares its reported board profile
with the package. Older or blank devices cannot identify themselves, so the
operator must physically confirm that the selected port belongs to an
ESP32-C6-DevKitC-1. The uploader probes the chip before writing and refuses a
different ESP32 family.

YATSS requests `TRACK_POWER:OFF`, closes its serial connection, probes the C6,
writes a merged image at flash offset `0x0`, resets the controller, reconnects,
and waits for the expected board profile and firmware version. A successful
write that does not produce the expected identity is reported as unverified.

## Uploader Acquisition

YATSS does not redistribute Espressif's uploader. It searches in this order:

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

## Firmware Package

The Windows project includes `YATSSMC\dist\*.yatssfw` in both build and publish
output under `Firmware`. A `.yatssfw` file is a ZIP containing:

- `manifest.json`, with package format, product, firmware version, board
  profile, chip, uploader backend, Arduino FQBN/core version, image name, flash
  offset, byte count, and SHA-256.
- One merged `.bin` firmware image.

YATSS validates package structure, board/chip/backend, image size, filename,
flash offset, and SHA-256 before presenting the update confirmation.

Build the C6 package from the repository root after changing the sketch or
firmware version:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Build-ControllerFirmware.ps1
```

The script requires Arduino CLI and the Espressif ESP32 Arduino core. It builds
`esp32:esp32:esp32c6`, creates the merged image, and replaces the versioned C6
package in `YATSSMC\dist`. Commit the package with the source that produced it.

## Failure Recovery

If probing or writing fails, YATSS reopens its normal serial loop and leaves
track power disabled in application state. Keep relay-coil and track power
disconnected, check the selected COM port and USB cable, then retry. Espressif
bootloader mode is in ROM, so a blank device or a device with an interrupted
application flash can normally be programmed again. Do not rely on software
power control while firmware is absent or being replaced.
