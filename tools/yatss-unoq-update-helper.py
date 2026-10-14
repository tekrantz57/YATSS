#!/usr/bin/env python3
"""Authenticated, loopback-only UNO Q deployment helper. Run as arduino, not root."""
import argparse
import hashlib
import hmac
import io
import json
import os
from pathlib import Path
import re
import secrets
import shlex
import shutil
import socket
import stat
import subprocess
import tempfile
import threading
import time
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PROFILE = "ARDUINO_UNO_Q_STM32U585"
FILES = (
    "app.yaml", "python/main.py", "sketch/sketch.ino", "sketch/sketch.yaml",
    "sketch/src/YatssController/FirmwareVersion.h",
    "sketch/src/YatssController/YatssController.h",
)
LIMIT = 2 * 1024 * 1024


def unpack(data):
    if len(data) > LIMIT:
        raise ValueError("Package exceeds 2 MiB")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        entries = archive.infolist()
        if sorted(e.filename for e in entries) != sorted((*FILES, "manifest.json")):
            raise ValueError("Package must contain exactly the supported UNO Q files")
        if sum(e.file_size for e in entries) > LIMIT or any(
                stat.S_ISLNK(e.external_attr >> 16) or e.flag_bits & 1 for e in entries):
            raise ValueError("Invalid package size, encryption, or symbolic link")
        manifest = json.loads(archive.read("manifest.json"))
        contents = {name: archive.read(name) for name in FILES}
    if manifest.get("schema") != 1 or manifest.get("profile") != PROFILE:
        raise ValueError("Unsupported UNO Q package")
    if manifest.get("files") != {name: hashlib.sha256(body).hexdigest() for name, body in contents.items()}:
        raise ValueError("Package checksum mismatch")
    version = re.search(rb'#define YATSSMC_FIRMWARE_VERSION "([^"\r\n]+)"',
                        contents[FILES[4]])
    if not version or version[1].decode() != manifest.get("version"):
        raise ValueError("Firmware version does not match package")
    if b"YATSS UNO Q Controller" not in contents["app.yaml"]:
        raise ValueError("Not a YATSS UNO Q app")
    return manifest["version"], contents


def frame(body):
    checksum = 0
    for byte in body.encode("ascii"):
        checksum ^= byte
    return f"{body}*{checksum:02X}\n".encode("ascii")


def decode(line):
    try:
        body, checksum = line.decode("ascii").strip().rsplit("*", 1)
        return body if frame(body).decode().strip().rsplit("*", 1)[1] == checksum.upper() else None
    except (ValueError, UnicodeError):
        return None


def verify_controller(version=None, seconds=15):
    deadline = time.monotonic() + seconds
    error = "No verified controller response"
    while time.monotonic() < deadline:
        try:
            with socket.create_connection(("127.0.0.1", 45991), timeout=2) as connection:
                connection.settimeout(1)
                received = b""
                identity = heartbeat = off = False
                next_send = 0
                while time.monotonic() < deadline:
                    if time.monotonic() >= next_send:
                        connection.sendall(frame("TRACK_POWER:OFF") + frame("PING") + frame("KEEPALIVE"))
                        next_send = time.monotonic() + 1
                    try:
                        chunk = connection.recv(4096)
                    except socket.timeout:
                        continue
                    if not chunk:
                        raise OSError("Controller closed connection")
                    received += chunk
                    if len(received) > 16384:
                        raise ValueError("Controller response too large")
                    while b"\n" in received:
                        line, received = received.split(b"\n", 1)
                        body = decode(line)
                        if body is None:
                            continue
                        fields = body.split(":")
                        if body.startswith("HELLO:YATSSMC:"):
                            if len(fields) != 7 or fields[2:5] != ["4", "8", PROFILE]:
                                raise ValueError("Connected device is not the supported UNO Q controller")
                            identity = version is None or fields[5] == version
                        heartbeat |= body.startswith("HEARTBEAT:")
                        off |= body in ("HELLO:TRACK_POWER:OFF", "HELLO:TRACK_POWER:MASK:00")
                        if identity and heartbeat and off:
                            return
        except OSError as exc:
            error = str(exc)
            time.sleep(0.5)
    raise RuntimeError("Controller identity/heartbeat/power-off verification failed: " + error)


class Updater:
    def __init__(self, app, state, cli="arduino-app-cli"):
        self.app, self.state, self.cli = Path(app).absolute(), Path(state).absolute(), cli
        self.lock = threading.Lock()
        self.status = {"state": "idle", "messages": []}

    def log(self, message):
        self.status["messages"] = (self.status["messages"] + [message])[-100:]
        print(message, flush=True)

    def run(self, *args, check=True, timeout=600):
        # File-backed output avoids unbounded memory use from compiler output.
        with tempfile.TemporaryFile() as output:
            result = subprocess.run(args, stdout=output, stderr=subprocess.STDOUT,
                                    timeout=timeout, check=False)
            output.seek(0, 2)
            output.seek(max(0, output.tell() - 12000))
            text = output.read().decode(errors="replace")
        if result.returncode and check:
            raise RuntimeError(f"{args[0]} failed ({result.returncode}): {text}")
        return result.returncode

    def preflight(self):
        if not (self.app / "app.yaml").is_file():
            raise ValueError("Configured App Lab source directory is missing")
        # Never write through symlinks, including parent directories.
        for name in FILES:
            target = self.app / name
            for part in (target, *target.parents):
                if part.is_symlink():
                    raise ValueError("App source paths must not contain symbolic links")
            if not target.is_file():
                raise ValueError("Missing installed controller source: " + name)
        if "name: YATSS UNO Q Controller" not in (self.app / "app.yaml").read_text():
            raise ValueError("Configured source is not the YATSS UNO Q app")
        if any(shutil.which(command) is None for command in (self.cli, "systemctl", "ss")):
            raise ValueError("Arduino App CLI, systemctl, and ss are required")
        self.run("systemctl", "--user", "show-environment", timeout=15)
        if shutil.disk_usage(self.app).free < 100 * 1024 * 1024:
            raise ValueError("Less than 100 MiB free on app source filesystem")

    def supervisor_active(self):
        result = self.run("systemctl", "--user", "is-active", "--quiet",
                          "yatss-unoq-controller.service", check=False, timeout=15)
        if result not in (0, 3, 4):
            raise RuntimeError("Cannot determine controller supervisor state")
        active = result == 0
        if active:
            result = subprocess.run(["systemctl", "--user", "show", "yatss-unoq-controller.service",
                                     "--property=Environment", "--value"], check=True,
                                    capture_output=True, text=True, timeout=15)
            environment = dict(item.split("=", 1) for item in shlex.split(result.stdout) if "=" in item)
            if Path(environment.get("YATSS_UNOQ_APP", "")).absolute() != self.app:
                raise ValueError("Supervisor source differs from helper --app; configure the same directory")
        return active

    def wait_stopped(self):
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            result = subprocess.run(["ss", "-H", "-ltn", "sport = :45991"],
                                    check=True, capture_output=True, text=True, timeout=5)
            if not result.stdout.strip():
                return
            time.sleep(0.5)
        raise RuntimeError("Controller TCP listener is still running; refusing to replace source")

    def start(self, data):
        version, contents = unpack(data)
        self.preflight()
        if not self.lock.acquire(blocking=False):
            raise ValueError("Another update is running")
        job = secrets.token_hex(16)
        self.status = {"state": "running", "job": job, "version": version, "messages": []}
        threading.Thread(target=self.deploy, args=(version, contents), daemon=False).start()
        return dict(self.status)

    def deploy(self, version, contents):
        backup = None
        backup_ready = False
        stopped = False
        try:
            self.log("Verifying UNO Q identity, heartbeat, and power off")
            verify_controller()
            active = self.supervisor_active()
            self.log("Stopping controller supervisor and App Lab runtime")
            stopped = True
            if active:
                self.run("systemctl", "--user", "stop", "yatss-unoq-controller.service", timeout=90)
            self.run(self.cli, "app", "stop", str(self.app), timeout=90)
            self.wait_stopped()
            self.state.mkdir(parents=True, exist_ok=True, mode=0o700)
            backup = Path(tempfile.mkdtemp(prefix="previous-", dir=self.state))
            for name in FILES:
                destination = backup / name
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(self.app / name, destination)
            backup_ready = True
            self.log("Previous source retained at " + str(backup))
            for name, body in contents.items():
                target = self.app / name
                temporary = target.with_name(target.name + ".yatss-update")
                with temporary.open("xb") as output:
                    output.write(body)
                os.replace(temporary, target)
            self.log("Compiling, flashing, and starting the updated App Lab controller")
            if active:
                self.run("systemctl", "--user", "start", "yatss-unoq-controller.service", timeout=30)
            else:
                self.run(self.cli, "app", "start", str(self.app))
            self.log("Waiting for updated firmware identity, heartbeat, and power-off acknowledgement")
            verify_controller(version, seconds=600 if active else 30)
            self.status["state"] = "complete"
            self.log("Update verified. Track power remains off; return to YATSS to resume manually.")
        except Exception as exc:
            self.log("Update failed: " + str(exc))
            if stopped:
                for command in (("systemctl", "--user", "stop", "yatss-unoq-controller.service"),
                                (self.cli, "app", "stop", str(self.app))):
                    try:
                        self.run(*command, timeout=90)
                    except Exception as stop_error:
                        self.log("Could not confirm runtime stopped: " + str(stop_error))
                self.log("Runtime left stopped where possible. Disconnect physical track power. "
                         "MCU firmware has NOT been rolled back.")
                self.log("Previous source retained at " + str(backup) if backup_ready else
                         "Installation did not begin; no complete source backup was created.")
            self.status["state"] = "failed"
        finally:
            self.lock.release()


def create_server(updater, token, port):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass

        def reply(self, code, data):
            body = json.dumps(data).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def authorized(self):
            return hmac.compare_digest(self.headers.get("Authorization", ""), "Bearer " + token)

        def do_GET(self):
            if not self.authorized():
                self.reply(401, {"error": "Authentication required"})
                return
            if self.path == "/status":
                self.reply(200, dict(updater.status))
            elif self.path == "/preflight":
                try:
                    updater.preflight()
                    self.reply(200, {"profile": PROFILE, "app": str(updater.app), "port": 45991})
                except Exception as exc:
                    self.reply(400, {"error": str(exc)})
            else:
                self.reply(404, {"error": "Not found"})

        def do_POST(self):
            self.connection.settimeout(15)
            if not self.authorized():
                self.reply(401, {"error": "Authentication required"})
                return
            try:
                length = int(self.headers.get("Content-Length", "0"))
                if self.path != "/update" or not 0 < length <= LIMIT:
                    raise ValueError("Invalid request or package size")
                data = self.rfile.read(length)
                if len(data) != length:
                    raise ValueError("Incomplete upload")
                self.reply(202, updater.start(data))
            except Exception as exc:
                self.reply(400, {"error": str(exc)})
    return ThreadingHTTPServer(("127.0.0.1", port), Handler)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", type=Path, default=Path.home() / "ArduinoApps/YATSSUnoQ")
    parser.add_argument("--state", type=Path, default=Path.home() / ".local/share/YATSS/UnoQUpdates")
    parser.add_argument("--port", type=int, default=38593)
    args = parser.parse_args()
    if os.getuid() != 1000:
        parser.error("Run as the UNO Q arduino user (UID 1000), never as root")
    os.umask(0o077)
    args.state.mkdir(parents=True, exist_ok=True, mode=0o700)
    import fcntl
    instance_lock = (args.state / "helper.lock").open("a")
    try:
        fcntl.flock(instance_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        parser.error("A UNO Q updater already owns this state directory")
    token_file = args.state / "token"
    if not token_file.exists():
        descriptor = os.open(token_file, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as output:
            output.write(secrets.token_hex(32))
    if token_file.is_symlink() or token_file.stat().st_mode & 0o077:
        parser.error("Token must be a private, non-symlink file (chmod 600)")
    token = token_file.read_text().strip()
    if not re.fullmatch(r"[0-9a-f]{64}", token):
        parser.error("Invalid token file")
    print(f"UNO Q updater listening on 127.0.0.1:{args.port}; token file: {token_file}", flush=True)
    create_server(Updater(args.app, args.state), token, args.port).serve_forever()


if __name__ == "__main__":
    main()
