#!/usr/bin/env python3
"""Loopback-only native speech bridge for YATSS."""

import argparse
import io
import json
import os
from pathlib import Path
import shutil
import socketserver
import subprocess
import tempfile
import threading
import wave


PROTOCOL_VERSION = 1
DEFAULT_PORT = 38591
MAX_REQUEST_BYTES = 8192
MAX_TEXT_LENGTH = 1000


class EspeakSpeechEngine:
    def __init__(self, executable: str):
        resolved = shutil.which(executable)
        if not resolved:
            raise RuntimeError(f"Speech executable not found: {executable}")
        self.executable = resolved
        self.lock = threading.Lock()

    def voices(self) -> list[str]:
        result = subprocess.run(
            [self.executable, "--voices"],
            check=True,
            capture_output=True,
            text=True,
            timeout=10,
        )
        voices: list[str] = []
        for line in result.stdout.splitlines()[1:]:
            fields = line.split()
            if len(fields) >= 4:
                name = fields[1].strip()
                if name and name not in voices:
                    voices.append(name)
        return sorted(voices, key=str.casefold)

    def speak(self, text: str, voice: str, rate: int | None) -> None:
        if not text or len(text) > MAX_TEXT_LENGTH:
            raise ValueError(f"Text must contain 1-{MAX_TEXT_LENGTH} characters")

        command = [self.executable]
        if voice:
            command.extend(["-v", voice])
        if rate is not None:
            words_per_minute = max(80, min(320, 175 + (int(rate) * 15)))
            command.extend(["-s", str(words_per_minute)])
        command.append(text)

        with self.lock:
            subprocess.run(command, check=True, timeout=30)

    def warm_up(self, voice: str) -> None:
        return


class PiperSpeechEngine:
    def __init__(self, data_dir: str):
        try:
            from piper import PiperVoice, SynthesisConfig
        except ImportError as error:
            raise RuntimeError("Piper is not installed; run: python -m pip install piper-tts") from error

        self.piper_voice_type = PiperVoice
        self.synthesis_config_type = SynthesisConfig
        self.data_dir = Path(data_dir).expanduser().resolve()
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.lock = threading.Lock()
        self.active_name = ""
        self.active_voice = None
        available_voices = self.voices()
        if available_voices:
            self._load_voice(available_voices[0])

    def _models(self) -> dict[str, Path]:
        models: dict[str, Path] = {}
        for model_path in self.data_dir.rglob("*.onnx"):
            config_path = Path(f"{model_path}.json")
            if config_path.is_file():
                models.setdefault(model_path.stem, model_path)
        return models

    def voices(self) -> list[str]:
        return sorted(self._models(), key=str.casefold)

    def _load_voice(self, voice_name: str):
        models = self._models()
        if not models:
            raise RuntimeError(
                f"No Piper voices found in {self.data_dir}; "
                "download one with python -m piper.download_voices"
            )

        selected_name = voice_name.strip()
        if not selected_name:
            selected_name = sorted(models, key=str.casefold)[0]
        else:
            selected_name = next(
                (name for name in models if name.casefold() == selected_name.casefold()),
                selected_name,
            )
        model_path = models.get(selected_name)
        if model_path is None:
            raise ValueError(f"Piper voice not found: {voice_name}")

        if self.active_voice is None or self.active_name != selected_name:
            self.active_voice = self.piper_voice_type.load(model_path)
            self.active_name = selected_name
        return self.active_voice

    def speak(self, text: str, voice: str, rate: int | None) -> None:
        if not text or len(text) > MAX_TEXT_LENGTH:
            raise ValueError(f"Text must contain 1-{MAX_TEXT_LENGTH} characters")

        with self.lock:
            piper_voice = self._load_voice(voice)
            length_scale = None
            if rate is not None:
                length_scale = max(0.5, min(2.0, 1.0 - (int(rate) * 0.08)))
            synthesis_config = self.synthesis_config_type(length_scale=length_scale)
            audio = io.BytesIO()
            with wave.open(audio, "wb") as wav_file:
                piper_voice.synthesize_wav(text, wav_file, syn_config=synthesis_config)
            self._play_wav(audio.getvalue())

    def warm_up(self, voice: str) -> None:
        with self.lock:
            self._load_voice(voice)

    @staticmethod
    def _play_wav(audio: bytes) -> None:
        if os.name == "nt":
            import winsound

            winsound.PlaySound(audio, winsound.SND_MEMORY | winsound.SND_SYNC)
            return

        players = (
            ("pw-play", []),
            ("paplay", []),
            ("aplay", ["-q"]),
            ("ffplay", ["-nodisp", "-autoexit", "-loglevel", "error"]),
        )
        selected = next(((shutil.which(name), args) for name, args in players if shutil.which(name)), None)
        if selected is None:
            raise RuntimeError("No supported audio player found (pw-play, paplay, aplay, or ffplay)")

        executable, arguments = selected
        temporary_path = ""
        try:
            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as temporary:
                temporary.write(audio)
                temporary_path = temporary.name
            subprocess.run([executable, *arguments, temporary_path], check=True, timeout=30)
        finally:
            if temporary_path:
                try:
                    os.unlink(temporary_path)
                except FileNotFoundError:
                    pass


class SpeechRequestHandler(socketserver.StreamRequestHandler):
    def handle(self) -> None:
        raw_request = self.rfile.readline(MAX_REQUEST_BYTES + 1)
        if len(raw_request) > MAX_REQUEST_BYTES:
            self.respond(ok=False, error="Request is too large")
            return

        try:
            request = json.loads(raw_request.decode("utf-8"))
            if request.get("protocol") != PROTOCOL_VERSION:
                raise ValueError("Unsupported protocol version")

            command = request.get("command")
            if command == "voices":
                self.respond(ok=True, voices=self.server.engine.voices())
            elif command == "warmup":
                self.server.engine.warm_up(str(request.get("voice", "")))
                self.respond(ok=True)
            elif command == "speak":
                self.server.engine.speak(
                    str(request.get("text", "")),
                    str(request.get("voice", "")),
                    request.get("rate"),
                )
                self.respond(ok=True)
            elif command == "ping":
                self.respond(ok=True)
            else:
                raise ValueError("Unknown command")
        except Exception as error:
            self.respond(ok=False, error=str(error))

    def respond(self, **response: object) -> None:
        payload = json.dumps(response, ensure_ascii=True, separators=(",", ":"))
        self.wfile.write(payload.encode("utf-8") + b"\n")


class SpeechServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(
        self,
        address: tuple[str, int],
        engine: EspeakSpeechEngine | PiperSpeechEngine,
    ):
        super().__init__(address, SpeechRequestHandler)
        self.engine = engine


def default_piper_data_dir() -> str:
    configured = os.environ.get("YATSS_PIPER_VOICE_DIR")
    if configured:
        return configured
    if os.name == "nt":
        local_app_data = os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData" / "Local"))
        return str(Path(local_app_data) / "YATSS" / "PiperVoices")
    xdg_data_home = os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local" / "share"))
    return str(Path(xdg_data_home) / "YATSS" / "PiperVoices")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--engine", choices=("espeak", "piper"), default="espeak")
    parser.add_argument("--espeak", default="espeak-ng")
    parser.add_argument("--data-dir", default=default_piper_data_dir())
    args = parser.parse_args()

    engine = (
        PiperSpeechEngine(args.data_dir)
        if args.engine == "piper"
        else EspeakSpeechEngine(args.espeak)
    )
    with SpeechServer(("127.0.0.1", args.port), engine) as server:
        print(
            f"YATSS {args.engine} speech helper listening on 127.0.0.1:{args.port}",
            flush=True,
        )
        server.serve_forever()


if __name__ == "__main__":
    main()
