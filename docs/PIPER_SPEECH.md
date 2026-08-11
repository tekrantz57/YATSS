# Piper Speech

YATSS can use Piper for higher-quality local speech on native Windows or while
running under Wine. Piper runs as a separate Python helper and keeps its active
voice model loaded between announcements. YATSS communicates with it only over
IPv4 loopback at `127.0.0.1:38592`.

Piper is optional. Windows SAPI, the Linux eSpeak NG helper, silent countdowns,
serial communication, and race timing remain independent of it.

## Windows Setup

Install Python 3.10 or newer, then install Piper:

```powershell
python -m pip install piper-tts
```

Download a voice into YATSS's standard voice directory:

```powershell
$voiceDir = "$env:LOCALAPPDATA\YATSS\PiperVoices"
New-Item -ItemType Directory -Force $voiceDir | Out-Null
python -m piper.download_voices --download-dir $voiceDir en_US-lessac-medium
```

Start YATSS and select `Piper` in Configure. YATSS starts the packaged helper
with the `python` command, discovers models in the voice directory, and lists
their model names in the voice selector. Set `YATSS_PYTHON` before starting
YATSS if Python is installed under a command or full path other than `python`.

No separate helper terminal is required on native Windows. YATSS stops the
helper process during a normal application exit. YATSS also loads the selected
model silently on its background speech thread during startup and after speech
settings change, avoiding first-announcement model-loading delay.

## Linux And Wine Setup

Install Piper in the native Linux environment, not inside Wine. A dedicated
virtual environment works on distributions that protect the system Python:

```bash
python3 -m venv "$HOME/.local/share/YATSS/piper-venv"
"$HOME/.local/share/YATSS/piper-venv/bin/pip" install piper-tts
voice_dir="${XDG_DATA_HOME:-$HOME/.local/share}/YATSS/PiperVoices"
mkdir -p "$voice_dir"
"$HOME/.local/share/YATSS/piper-venv/bin/python" -m piper.download_voices \
  --download-dir "$voice_dir" en_US-lessac-medium
"$HOME/.local/share/YATSS/piper-venv/bin/python" \
  Linux/yatss-speech-helper.py --engine piper --port 38592 --data-dir "$voice_dir"
```

If Piper was installed with `pipx install piper-tts`, run the helper with the
interpreter inside pipx's isolated environment:

```bash
PIPX_VENVS="$(pipx environment --value PIPX_LOCAL_VENVS)"
"$PIPX_VENVS/piper-tts/bin/python" Linux/yatss-speech-helper.py \
  --engine piper \
  --port 38592 \
  --data-dir "${XDG_DATA_HOME:-$HOME/.local/share}/YATSS/PiperVoices"
```

Running the helper with the system `python3` after a pipx installation will
report that Piper is not installed because pipx intentionally isolates each
package from the system interpreter.

Leave the helper running, start YATSS under Wine, and select `Piper`. Piper and
eSpeak NG helpers may run together because eSpeak uses port `38591` and Piper
uses port `38592`.

The helper loads its first available Piper model before reporting that it is
listening. Wait for the listening message before starting YATSS; this ensures
the first `3` of the first countdown is not consumed by model startup.

For unattended startup, create a systemd user service using the resolved
virtual-environment Python path and the same helper arguments. The service must
run as the desktop user so generated audio reaches that user's audio session.

## Voice Directories

The defaults are:

```text
Windows: %LOCALAPPDATA%\YATSS\PiperVoices
Linux:   ${XDG_DATA_HOME:-$HOME/.local/share}/YATSS/PiperVoices
```

Set `YATSS_PIPER_VOICE_DIR` before starting YATSS or the helper to override the
default. Each voice requires both its `.onnx` model and adjacent `.onnx.json`
configuration file.

Piper's engine is GPL-3.0-or-later and remains a separately installed program;
it is not incorporated into the MIT-licensed YATSS executable. Voice models
have their own licenses. Review a model's `MODEL_CARD` before redistributing
it. YATSS does not package or redistribute Piper or a voice model.

## Engine Selection

- `Automatic` prefers a usable Windows SAPI voice, then Piper, then eSpeak NG.
- `Windows SAPI` uses only SAPI.
- `Piper` uses only the Piper helper.
- `eSpeak NG helper` uses only the helper on port `38591`.
- `None` disables speech while retaining the visual countdown.

If Piper has no voices in Configure, verify the model directory contains both
required files, restart the Configure dialog to refresh discovery, and test the
helper manually. On Wine, also verify that the native helper reports it is
listening on port `38592`.
