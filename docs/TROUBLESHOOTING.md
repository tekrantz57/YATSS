# Troubleshooting

## ESP32-C5 UART Port Does Not Appear

The Waveshare ESP32-C5-WIFI6-KIT-N16R8 uses a WCH CH343 USB-to-UART bridge.
Install the WCH driver, reconnect the board through its UART USB-C socket, and
select the resulting COM port in Arduino IDE or YATSS.

Use `ESP32C5 Dev Module` as the Arduino IDE board. Do not use the native USB
socket with the YATSS C5 pin profile because GPIO13 and GPIO14 are assigned to
track-power outputs. If Arduino IDE invokes `dfu-util`, reselect the ESP32C5
board and its CH343 COM port; C5 uploads use Espressif's `esptool`.

## ESP32-C6 UART Port Does Not Appear

The ESP32-C6-DevKitC-1 uses a Silicon Labs CP2102N bridge on the USB-C socket
labeled `UART`. Install the Silicon Labs CP210x VCP driver, reconnect the board,
and select the resulting `Silicon Labs CP210x USB to UART Bridge` port in
Arduino IDE. Windows assigns the COM number, so it may differ between systems.

Use `ESP32C6 Dev Module` as the Arduino IDE board. Do not use the board's socket
labeled `USB` with the YATSS C6 pin profile because GPIO12 and GPIO13 are
assigned to track-power outputs.

If an upload invokes `dfu-util` and reports `No DFU capable USB device
available`, Arduino IDE is still targeting `Arduino Nano ESP32`. Reselect
`ESP32C6 Dev Module` and the CP210x COM port. The C6 UART upload uses Espressif's
`esptool`, not `dfu-util`.

## Visual Studio Opens A Moved Project As Miscellaneous Files

After renaming project folders, Visual Studio may try to reopen a project from
its old path and show an error like:

```text
An error occurred in 'Miscellaneous Files' while attempting to open 'YATSS.csproj'
The document cannot be opened. It has been renamed, deleted, or moved.
```

Close Visual Studio and delete the ignored `YATSSWin\.vs` folder. Visual Studio
will recreate it from `YATSSWin\YATSS.sln` the next time the solution opens.

## Nano ESP32 Upload Fails With dfu-util Exit Status 74

If upload fails after a successful compile with:

```text
error get_status: LIBUSB_ERROR_PIPE
Failed uploading: uploading error: exit status 74
```

the sketch compiled, but `dfu-util` could not complete communication with the
Nano ESP32 bootloader.

Try this sequence:

1. Close the YATSS Windows app if it is connected to the board.
2. Close Arduino Serial Monitor, Serial Plotter, Arduino Cloud, and other tools
   using the board or COM port.
3. Unplug and reconnect the Nano ESP32 using a direct USB data cable.
4. Double-tap reset to put the board in bootloader mode.
5. Reselect the board port in Arduino IDE if the port changed.
6. Upload again.

Arduino documents `LIBUSB_ERROR_PIPE` and exit status 74 in its
[dfu-util upload errors guide](https://support.arduino.cc/hc/en-us/articles/11011849739804-dfu-util-errors-when-uploading-exit-status-74).
If uploads keep requiring bootloader mode or remain unreliable, Arduino's
[Nano ESP32 bootloader reset guide](https://support.arduino.cc/hc/en-us/articles/9810414060188-Reset-the-Arduino-bootloader-on-the-Nano-ESP32)
describes the full bootloader recovery process.
