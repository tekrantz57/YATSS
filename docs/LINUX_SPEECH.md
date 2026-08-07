# Linux Speech Under Wine

YATSS can use native Linux text-to-speech while the unchanged Windows
application runs under Wine. The Windows process connects to a small helper on
TCP `127.0.0.1:38591`; the helper invokes `espeak-ng` and acknowledges each
utterance after playback finishes.

The helper listens only on the IPv4 loopback interface. It does not accept
remote network connections, construct shell commands, or require root access.

## Speech Engine Selection

Configure offers these engines:

- `Automatic` uses Windows SAPI when at least one SAPI voice is installed. If
  SAPI has no voices, it tries the Linux helper.
- `Windows SAPI` uses only the existing Windows COM speech implementation.
- `Linux helper` uses only the loopback helper.
- `None` disables speech. The visual start lights and protected countdown still
  operate.

The separate `Enable voice announcements` setting remains the quick global
on/off control. `Automatic` is the default engine.

## Fedora Setup

The release publish directory includes the helper at
`Linux/yatss-speech-helper.py`.

1. Install Python and eSpeak NG:

   ```bash
   sudo dnf install python3 espeak-ng
   ```

2. Confirm that native speech reaches the desired audio output:

   ```bash
   espeak-ng "YATSS speech test"
   espeak-ng -v en-us -s 220 "3 2 1 let's go"
   ```

   eSpeak NG uses `-v` to select a voice and `-s` to set words per minute.
   The unsupported `--voice` and `--speed` forms must not be used.

3. From the extracted YATSS publish directory, start the helper as the same
   desktop user who runs Wine:

   ```bash
   python3 Linux/yatss-speech-helper.py
   ```

4. Start YATSS under Wine. In Configure, leave the engine on `Automatic` or
   select `Linux helper`. The voice list should contain eSpeak language codes
   such as `en`, `en-gb`, and `en-us`.

Keep the helper terminal open while YATSS is running. Stop it with `Ctrl+C`.

## Optional User Service

After verifying manual operation, the helper can run as a systemd user service.
Create `~/.config/systemd/user/yatss-speech-helper.service` and replace the
example script path with the absolute path to the extracted helper:

```ini
[Unit]
Description=YATSS Linux speech helper

[Service]
ExecStart=/usr/bin/python3 /absolute/path/to/YATSS/Linux/yatss-speech-helper.py
Restart=on-failure

[Install]
WantedBy=default.target
```

Enable and start it:

```bash
systemctl --user daemon-reload
systemctl --user enable --now yatss-speech-helper.service
systemctl --user status yatss-speech-helper.service
```

## Operation And Failure Behavior

Speech requests are serialized by YATSS. Countdown lamps are painted before
the corresponding `3`, `2`, and `1` utterances. The Linux helper response is
blocking, matching the existing SAPI behavior.

Native eSpeak NG announcements, voice selection, and the accelerated countdown
have been exercised successfully with the ARM64 YATSS build under Wine on an
Arduino UNO Q.

If the selected engine or helper fails, YATSS continues silently. Start and
restart countdowns retain a 1.5-second minimum and the visual lights still
operate. Serial communication, lap scoring, track-power commands, and reports
do not depend on the speech helper.

If no Linux voices appear:

1. Verify that the helper terminal says it is listening on
   `127.0.0.1:38591`.
2. Run `espeak-ng --voices` and confirm that it returns voices.
3. Change the Configure engine away from `Linux helper` and back to refresh
   discovery.
4. Check the helper terminal for an error from eSpeak or the audio system.

Only one helper may listen on port `38591` at a time.
