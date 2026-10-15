# TCP Test Controller

The simulator exercises the real YATSS TCP reader, protocol parser, scoring,
logging, and interruption/recovery logic without a controller attached. It is
a software test double, not a firmware emulator or an electrical timing test.
It has no GPIO, relay driver, firmware uploader, or remote-control service.

## Recommended Interactive Test

From the repository with the .NET 10 SDK on Windows:

```powershell
dotnet run --project YATSSWin/YATSS.Tests -c Release -- --tcp-test-ui
```

Or extract a matching self-contained validation package and run:

```powershell
./YATSS.Tests.exe --tcp-test-ui
```

On ARM64 Linux, use the ARM64 validation package in a working graphical Wine
session (a dedicated test Wine prefix is recommended):

```bash
wine YATSS.Tests.exe --tcp-test-ui
```

This launches the actual app, eight test racers, a 60-second heat race with
60-second intermissions, and its own loopback controller on a temporary port.
Voices and lap sounds start disabled. App databases, journals, reports, backups,
and serial logs use the separate test directory printed in the terminal.
The directory is retained after exit for inspection; it can be deleted when
no longer needed. Normal development app data is not used.

Wait for the controller to become ready. In the terminal enter `laps 3000`,
then press Space in the app to start the heat. The simulator emits crossings
every three seconds for lanes whose virtual power is on. Do not enable the
app's internal Demo Lap Stream: that would bypass the TCP path being tested.
Use the app's normal controls to configure other race formats or qualifying.

Try `offline` during a heat, wait for the app to pause, then enter `online`.
The app must reverify the controller and wait for the director's Space press;
it must not automatically resume. `silence on` tests a connection that stays
open but stops responding; `silence off` restores responses. `quit` or closing
the app window ends the isolated session.

## Terminal Commands

Lane numbers in commands are **1-8**, although protocol lane indexes are 0-7.

| Command | Effect |
| --- | --- |
| `help` / `status` | List commands / show clock, session, virtual power and counters |
| `edge 1` | Send one crossing, including when virtual power is off to test rejection |
| `bad-edge 1` | Send a crossing with an invalid checksum |
| `laps 3000` / `laps off` | Automatic crossings; minimum interval 250 ms |
| `disconnect` | Drop the client once; permit immediate reconnection |
| `offline` / `online` | Drop/refuse clients / permit reconnection |
| `silence on` / `silence off` | Suppress all outgoing frames / restore them |
| `heartbeat off` / `heartbeat on` | Suppress/restore periodic heartbeats only |
| `reset` | Rotate session, reset controller clock/sequences, set virtual power off |
| `logical-reset` | Rotate session and clear sequences/power, retaining clock |
| `silent-reset` / `silent-logical-reset` | Same resets without an immediate HELLO |
| `stale edge` / `stale heartbeat` / `stale hello` / `stale off` | Replay traffic from the preceding session; reset first |
| `advance 5000` | Add milliseconds to the wrapping 32-bit controller clock |
| `clock 4294966295` | Set clock near wrap; next ticks continue normally |
| `bad-checksum` | Send an invalid heartbeat checksum |
| `quit` | End the session |

Clock jumps intentionally can complete a heat/qualifier. The virtual five-second
host-command watchdog uses real elapsed time, so clock manipulation does not
accidentally manipulate its deadline. Automatic laps have a fixed interval and
no simulated car physics. Manual edges are not a physical debounce model.
Diagnostics supports basic session/status/sensor responses; relay pulses are
explicitly unsupported rather than pretending that a physical relay was tested.

## Standalone Controller

For development on Windows or Linux with the .NET 10 SDK:

```text
dotnet run --project YATSSWin/YATSS.Simulator -c Release
```

Default endpoint: `TCP:127.0.0.1:45992`. To choose another test port, append
`-- --port 45993`. It listens only on IPv4 loopback, accepts one active client,
and refuses an explicit port 45991 (reserved for the real UNO Q bridge).
Additional clients are rejected, not allowed to replace the active app.

Connecting an ordinary YATSS process to this endpoint uses that process's
**normal data**. Copying an app folder does not isolate its data. Prefer the
interactive test launcher above. The app marks simulator status messages with
`TCP TEST CONTROLLER` and refuses firmware flashing for its distinct identity.

The portable `YATSS.Protocol` library shares the app's existing parser and
checksum implementation. The simulator emits current protocol-5 session-aware
frames, validates command checksums and session-bound power requests, and keeps
its clock/session across ordinary client reconnects.

## Automated Coverage

The packaged regression runner includes a separate `tcp` suite. It checks real
socket identity/commands, one-client ownership, stale-command refusal and bad
checksums, then launches the real app against its own ephemeral simulator.
App checks cover scoring, same-session reconnect, frozen outage time, explicit
resume, first-resumed-crossing fast-lap suppression, unannounced logical resets,
retired frames, silence timeout, cancelled countdowns, and qualifying reruns
that preserve completed qualifiers. It never connects to a selected real device.

These tests caught and fixed a qualifying elapsed-time underflow: a confirmed
heartbeat just before the estimated start must mean zero elapsed time, not an
almost-full 32-bit wrap. Normal clock wrap remains tested.

Windows software results do not establish Wine compatibility or validate
physical sensor timestamps, relay safety, RouterBridge, or actual MCU resets.
See [software validation](SOFTWARE_VALIDATION.md) for building test packages.
