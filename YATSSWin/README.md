# YATSS Windows App

This is the Windows WinForms side of the slot-car lap timer. The app owns race
state, lap counting, filtering, logging, heat-race flow, qualifying, reports,
and track-power commands. The microcontroller only reports timestamped sensor
edges.

## Build And Test

From the `YATSSWin` directory:

```powershell
dotnet build YATSS.sln -c Release
dotnet run --project YATSS.Tests\YATSS.Tests.csproj -c Release
```

From the repository root:

```powershell
dotnet build YATSSWin\YATSS.sln -c Release
dotnet run --project YATSSWin\YATSS.Tests\YATSS.Tests.csproj -c Release
```

The app targets `.NET 9` for Windows Forms.

## Serial Connection

Configure the COM port from the app's Configure dialog. The selected port is
persisted locally. Serial traffic is logged under:

```text
%LOCALAPPDATA%\YATSS\logs\serial-YYYYMMDD.log
```

App settings, lane configuration, and racer names are stored in:

```text
%LOCALAPPDATA%\YATSS\laps.db
```

The `File > Serial Log` window tails the current log. It follows the end of the
file while scrolled to the bottom, pauses when you scroll up to inspect older
lines, and resumes following when you scroll back to the bottom.

While YATSS is running, the app asks Windows to keep the system and display
awake so the race board does not sleep or blank during timing. Normal power
management resumes when the app exits.

## Controller Diagnostics

`File > Controller Diagnostics` opens a live eight-lane wiring view when the
controller is connected and YATSS is idle in Practice mode. It displays each
raw sensor state, transition count, debounced accepted-edge count, track-power
state, controller uptime, debounce setting, and cumulative queue-overflow count.

Each lane has a controller-timed `Pulse Cut` relay test. Pulses only remove
power and automatically restore the previous mask. The window can also clear
transition counts, refresh controller status, or cut all track power. Diagnostic
sensor activity is logged but never enters the lap-counting path. Starting
another race mode closes diagnostics first.

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
%USERPROFILE%\Documents\YATSS Race Reports
```

## Demo Mode

The Mode menu includes demo options for exercising the app without relying on
live sensor hardware.

`Demo Race` seeds a heat race with sample racers so the heat-race workflow can
be tested quickly.

`Demo Lap Stream` generates simulated controller heartbeats and lane edges. It
uses the same lap-processing path as real serial input, but ignores real serial
lines while the demo stream is active. This is useful for testing timing board
updates, heat transitions, qualifying and race setup behavior, and report
output.

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
