# MKTS Windows App

This is the Windows WinForms side of the slot-car lap timer. The app owns race
state, lap counting, filtering, logging, heat-race flow, qualifying, reports,
and track-power commands. The microcontroller only reports timestamped sensor
edges.

## Build And Test

From the `MKTSWin` directory:

```powershell
dotnet build MKTS.sln -c Release
dotnet run --project MKTS.Tests\MKTS.Tests.csproj -c Release
```

From the repository root:

```powershell
dotnet build MKTSWin\MKTS.sln -c Release
dotnet run --project MKTSWin\MKTS.Tests\MKTS.Tests.csproj -c Release
```

The app targets `.NET 9` for Windows Forms.

## Serial Connection

Configure the COM port from the app's Configure dialog. The selected port is
persisted locally. Serial traffic is logged under:

```text
%LOCALAPPDATA%\MKTS\logs\serial-YYYYMMDD.log
```

The `File > Serial Log` window tails the current log. It follows the end of the
file while scrolled to the bottom, pauses when you scroll up to inspect older
lines, and resumes following when you scroll back to the bottom.

## Practice Mode

Practice is the default mode. Edges from active lanes establish a baseline on
the first pass, then subsequent valid edges count laps. The app filters laps
using:

- Minimum lap time
- Windows raw edge lockout
- Controller-side debounce
- Active lane count

The first edge on a lane establishes the baseline. The board shows `0` laps
after the baseline and keeps timing fields blank until a real counted lap is
available.

## Heat Race Mode

Heat race setup supports:

- Race name
- Heat length in minutes
- Time between heats
- Active lane count from 2 through 8
- More racers than lanes
- Lane names and colors
- Optional qualifying

Space starts a heat, pauses a running heat for a track call, and resumes a
paused heat. Track power is cut immediately on track calls and restored after
the spoken countdown. During stoppage time, lap counts can be adjusted by lane.

The app writes heat-race reports as HTML files under:

```text
%USERPROFILE%\Documents\Laps Race Reports
```

## Configure Dialog

Current persisted settings include:

- Serial port
- Minimum lap time
- Track length in feet
- Active lane count
- Lane names and colors
- SAPI speech voice
- Sound on too-fast laps
- Controller sensor debounce
- Windows raw edge lockout

Changing controller sensor debounce sends `CONFIG:DEBOUNCE:<milliseconds>` to
the controller when connected.

## Track Power

The app sends track-power commands over serial. In practice mode it enables the
configured active lanes. In heat-race mode it enables only occupied lanes and
cuts power during intermissions and track calls.

See `..\docs\SERIAL_PROTOCOL.md` for the serial protocol and
`..\docs\TROUBLESHOOTING.md` for Visual Studio and upload recovery notes.
