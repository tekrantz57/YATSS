# UNO Q Updates From YATSS

`File > Update Controller Firmware...` can update the UNO Q's STM32 sketch
and Linux bridge together. This first version supports YATSS running locally
under Wine on the UNO Q, connected to `TCP:127.0.0.1:45991`. Remote Windows
updates and initial provisioning of a blank MCU are not supported. Install and
run the App Lab controller once before using this updater.

## One-Time Setup

Run these commands from the UNO Q desktop terminal as **arduino** (UID 1000),
not root or krantz. Change into the extracted YATSS publish directory first.
Python 3, `ss`, a working user systemd session, Docker, Arduino App CLI, the
UNO Q board platform, and the App Lab dependencies must already work.

```bash
mkdir -p ~/.local/bin ~/.config/systemd/user
install -m 700 Linux/yatss-unoq-update-helper.py ~/.local/bin/
install -m 600 Linux/Services/yatss-unoq-updater.service ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now yatss-unoq-updater.service
cat ~/.local/share/YATSS/UnoQUpdates/token
```

The token file is created on first helper startup. If `cat` runs before startup
finishes, wait a moment and retry. Treat this token as a password; do not post
it in screenshots, logs, or issue reports. YATSS asks for it for each update
and does not save it in the database. It is sent only to the fixed local
address `127.0.0.1:38593`; proxies and redirects are disabled.

The service defaults to the editable App Lab source directory
`~/ArduinoApps/YATSSUnoQ`. It must contain `app.yaml`, `python/main.py`, and
the complete `sketch` tree, not a ZIP or a `user:<name>` reference. It must be
the directory actually used by the controller runtime. If your App Lab import
uses a different directory, configure the helper with a systemd override:

```bash
systemctl --user edit yatss-unoq-updater.service
```

Use the actual absolute source path:

```ini
[Service]
ExecStart=
ExecStart=/usr/bin/python3 /home/arduino/.local/bin/yatss-unoq-update-helper.py --app /home/arduino/ArduinoApps/YATSSUnoQ
```

Then restart the updater service. If the optional
`yatss-unoq-controller.service` boot supervisor is running, its
`YATSS_UNOQ_APP` environment setting must point to that exact same source
directory. See [controller startup](UNO_Q_CONTROLLER.md). The helper refuses
an active supervisor pointing elsewhere rather than updating an unused copy.
Do not open, edit, start, stop, or close App Lab during an update.

## Update Procedure

1. Return to Practice mode. Stop simulated laps and close diagnostics.
2. Physically disconnect track power and relay-coil power. Leave the UNO Q
   itself powered, and keep its Linux desktop running.
3. Choose `File > Update Controller Firmware...`, enter the helper token,
   and confirm the firmware version and safety warning.
4. Leave YATSS open while the progress window reports the update. First-time
   compilation or container preparation can take several minutes.
5. After **update verified**, YATSS reconnects and checks the expected firmware
   identity, a heartbeat, and a power-off acknowledgement. Power is not restored
   automatically. Verify wiring and controller readiness before reconnecting
   physical power and resuming manually.

The bundled source version is read from the shared firmware version header.
No new firmware protocol or pin mapping is introduced by this feature.

## What The Helper Does

- Listens on IPv4 loopback only and requires a private, randomly generated
  token on every request. It runs as arduino, without sudo.
- Accepts only the six known controller source files plus a versioned manifest.
  It checks per-file SHA-256 hashes, package size, duplicate/unexpected paths,
  and symbolic links. Hashes detect corruption, not an untrusted publisher:
  use only trusted YATSS releases, since the bridge is executable Python code.
- Checks the installed source, available tools, user service manager, and at
  least 100 MiB free on the source filesystem. Arduino compilation and Docker
  may need considerably more space elsewhere; this is not a full disk-space
  guarantee.
- After YATSS releases its TCP connection, confirms the UNO Q profile,
  heartbeat, and a fresh power-off response before stopping anything.
- Stops an active boot supervisor and the App Lab runtime, and confirms that
  TCP port 45991 is no longer listening before replacing source.
- Retains the previous six source files under
  `~/.local/share/YATSS/UnoQUpdates/previous-*`. Backups are not automatically
  deleted. They do not contain the race database or a complete Docker image.
- Installs the new source and restarts the previous supervisor, or calls
  `arduino-app-cli app start <source-directory>` directly if no supervisor was
  active. App CLI handles preparation, compilation, MCU flashing, and bridge
  deployment; the helper does not implement a second flasher.
- Verifies the updated controller over TCP, closes its temporary connection,
  and lets YATSS reconnect. Only one update can run at a time.

Arduino's official [start command source](https://github.com/arduino/arduino-app-cli/blob/main/cmd/arduino-app-cli/app/start.go)
and [stop command source](https://github.com/arduino/arduino-app-cli/blob/main/cmd/arduino-app-cli/app/stop.go)
describe the editable-app lifecycle used here. This integration still requires
an end-to-end bench update on the installed App CLI version.

## Failure And Recovery

Keep physical track power disconnected on any failure. If a failure occurs
after stopping begins, the helper attempts to stop both runtime and supervisor
again. The previous source remains available for recovery, but **this does not
roll back MCU firmware**. The helper does not silently reflash the old version
or resume a race. If stopping itself fails, its log explicitly reports that
the stopped state could not be confirmed.

An HTTP timeout or lost YATSS connection does not cancel the Linux update.
YATSS leaves its controller connection suspended after an unsuccessful update
submission so it cannot steal the helper's verification connection. Check:

```bash
systemctl --user status yatss-unoq-updater.service
journalctl --user -u yatss-unoq-updater.service -n 100 --no-pager
journalctl --user -u yatss-unoq-controller.service -n 100 --no-pager
```

Do not retry, reboot, or restart YATSS until the helper has finished. After a
completed failure, inspect the retained source and App CLI errors, correct the
cause, and manually deploy a known-good App Lab package if needed. Then
restart YATSS and verify identity, heartbeat, and power-off readiness before
racing. If the helper itself was killed or power was lost, there is no claim
of a completed transaction; treat the controller as requiring manual recovery.

The controller service remains enabled if it was enabled before. After a
failed update it is stopped for the current session, but a later reboot may
start it again using the installed source. Do not reconnect physical track
power merely because the machine has rebooted.

## Developer Verification

```bash
python3 -m unittest discover -s tools/tests -p test_unoq_update.py -v
```

The .NET regression suite includes `unoq-update` for bundle hashes,
authentication, local addressing, HTTP errors, and completion handling.
Python tests exercise package rejection, checksummed frames, authentication,
concurrent-update rejection, and mocked deployment/failure ordering. Neither
test suite flashes hardware. Actual MCU flashing, supervisor integration,
power-off behavior through reset, and Wine update UI still need bench testing.
