# Troubleshooting

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
