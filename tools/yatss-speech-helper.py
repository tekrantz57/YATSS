#!/usr/bin/env python3
"""Loopback-only eSpeak NG bridge for YATSS running under Wine."""

import argparse
import json
import shutil
import socketserver
import subprocess
import threading


PROTOCOL_VERSION = 1
DEFAULT_PORT = 38591
MAX_REQUEST_BYTES = 8192
MAX_TEXT_LENGTH = 1000


class SpeechEngine:
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
            command.extend(["--voice", voice])
        if rate is not None:
            words_per_minute = max(80, min(320, 175 + (int(rate) * 15)))
            command.extend(["--speed", str(words_per_minute)])
        command.append(text)

        with self.lock:
            subprocess.run(command, check=True, timeout=30)


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

    def __init__(self, address: tuple[str, int], engine: SpeechEngine):
        super().__init__(address, SpeechRequestHandler)
        self.engine = engine


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--espeak", default="espeak-ng")
    args = parser.parse_args()

    engine = SpeechEngine(args.espeak)
    with SpeechServer(("127.0.0.1", args.port), engine) as server:
        print(f"YATSS speech helper listening on 127.0.0.1:{args.port}", flush=True)
        server.serve_forever()


if __name__ == "__main__":
    main()
