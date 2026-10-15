import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch
import urllib.error
import urllib.request
import zipfile

spec = importlib.util.spec_from_file_location("updater", Path(__file__).parents[1] / "yatss-unoq-update-helper.py")
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def package(extra=None, bad_hash=False):
    source = Path(__file__).parents[2] / "YATSSUnoQ"
    contents = {name: (source / name).read_bytes() for name in helper.FILES}
    version = helper.re.search(rb'YATSSMC_FIRMWARE_VERSION "([^"]+)"', contents[helper.FILES[4]])[1].decode()
    manifest = {"schema": 1, "profile": helper.PROFILE, "version": version,
                "files": {name: hashlib.sha256(body).hexdigest() for name, body in contents.items()}}
    if bad_hash:
        manifest["files"]["app.yaml"] = "0" * 64
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w") as archive:
        for name, body in contents.items():
            archive.writestr(name, body)
        archive.writestr("manifest.json", json.dumps(manifest))
        if extra:
            archive.writestr(extra, "bad")
    return stream.getvalue(), contents


class UpdateTests(unittest.TestCase):
    def test_package_whitelist_and_hash(self):
        data, files = package()
        version, unpacked = helper.unpack(data)
        self.assertTrue(version)
        self.assertEqual(files, unpacked)
        for data in (package("../escape")[0], package(bad_hash=True)[0], b"not a zip", b"x" * (helper.LIMIT + 1)):
            with self.assertRaises((ValueError, zipfile.BadZipFile)):
                helper.unpack(data)

    def test_frames(self):
        self.assertEqual(helper.decode(helper.frame("TRACK_POWER:OFF")), "TRACK_POWER:OFF")
        self.assertIsNone(helper.decode(b"HEARTBEAT:5*00"))
        self.assertIsNone(helper.decode(b"unframed"))

    def test_controller_verification_requires_all_three_responses(self):
        class Connection:
            def __init__(self, responses):
                self.responses = responses
                self.sent = []
            def __enter__(self):
                return self
            def __exit__(self, *_args):
                pass
            def settimeout(self, _timeout):
                pass
            def sendall(self, data):
                self.sent.append(data)
            def recv(self, _size):
                return self.responses
        session = ":1234567890ABCDEF"
        identity = helper.frame("HELLO:YATSSMC:5:8:" + helper.PROFILE + ":test:0" + session)
        heartbeat = helper.frame("HEARTBEAT:1234" + session)
        off = helper.frame("HELLO:TRACK_POWER:OFF" + session)
        connection = Connection(identity + heartbeat + off)
        with patch.object(helper.socket, "create_connection", return_value=connection):
            helper.verify_controller("test", seconds=1)
        self.assertIn(helper.frame("TRACK_POWER:OFF"), connection.sent[0])
        legacy = helper.frame("HELLO:YATSSMC:4:8:" + helper.PROFILE + ":old:0") + \
            helper.frame("HEARTBEAT:1234") + helper.frame("HELLO:TRACK_POWER:OFF")
        with patch.object(helper.socket, "create_connection", return_value=Connection(legacy)):
            helper.verify_controller(seconds=1)
        with patch.object(helper.socket, "create_connection", return_value=Connection(legacy)), \
                self.assertRaisesRegex(ValueError, "not the supported"):
            helper.verify_controller("test", seconds=1)
        for responses, version in ((identity + heartbeat, "test"), (identity + off, "test"),
                                   (heartbeat + off, "test"), (identity + heartbeat + off, "wrong"),
                                   (identity + off + helper.frame("HEARTBEAT:1234:1111111111111111") + heartbeat, "test"),
                                   (identity + heartbeat + helper.frame("HELLO:TRACK_POWER:OFF:1111111111111111"), "test")):
            with patch.object(helper.socket, "create_connection", return_value=Connection(responses)), \
                    self.assertRaisesRegex(RuntimeError, "verification failed"):
                helper.verify_controller(version, seconds=0.02)

    def test_preflight_rejects_missing_source(self):
        with tempfile.TemporaryDirectory() as root:
            updater = self.setup_updater(root)
            (updater.app / "python/main.py").unlink()
            with self.assertRaisesRegex(ValueError, "Missing installed"):
                updater.preflight()

    def test_supervisor_source_mismatch_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            updater = self.setup_updater(root)
            result = helper.subprocess.CompletedProcess([], 0, stdout="YATSS_UNOQ_APP=/different/app")
            with patch.object(updater, "run", return_value=0), \
                    patch.object(helper.subprocess, "run", return_value=result), \
                    self.assertRaisesRegex(ValueError, "Supervisor source differs"):
                updater.supervisor_active()

    def setup_updater(self, root):
        app = Path(root) / "app"
        _, files = package()
        for name, body in files.items():
            target = app / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(body)
        return helper.Updater(app, Path(root) / "state")

    def test_deployment_and_failure_safety(self):
        for active, fail_at in ((False, None), (True, None), (False, 0), (False, 1)):
            with self.subTest(active=active, fail_at=fail_at), tempfile.TemporaryDirectory() as root:
                updater = self.setup_updater(root)
                calls = []
                def verify(*args, **kwargs):
                    calls.append(("verify", *args))
                    if fail_at == sum(call[0] == "verify" for call in calls) - 1:
                        raise RuntimeError("simulated verification failure")
                def run(*args, **kwargs):
                    calls.append(args)
                    return 0
                updater.status = {"state": "running", "messages": []}
                updater.lock.acquire()
                version, files = helper.unpack(package()[0])
                with patch.object(helper, "verify_controller", side_effect=verify), \
                        patch.object(updater, "run", side_effect=run), \
                        patch.object(updater, "wait_stopped"), \
                        patch.object(updater, "supervisor_active", return_value=active):
                    updater.deploy(version, files)
                self.assertEqual(updater.status["state"], "complete" if fail_at is None else "failed")
                self.assertFalse(updater.lock.locked())
                self.assertEqual(calls[0], ("verify",))
                if fail_at == 0:
                    self.assertEqual(len(calls), 1, "must not stop or write before verifying identity and OFF")
                    self.assertFalse(updater.state.exists())
                else:
                    backups = list(updater.state.glob("previous-*"))
                    self.assertEqual(len(backups), 1)
                    self.assertEqual((backups[0] / "app.yaml").read_bytes(), files["app.yaml"])
                    if fail_at == 1:
                        self.assertIn(("arduino-app-cli", "app", "stop", str(updater.app)), calls[-2:])

    def test_concurrent_update_refused(self):
        with tempfile.TemporaryDirectory() as root:
            updater = self.setup_updater(root)
            updater.lock.acquire()
            with patch.object(updater, "preflight"), self.assertRaisesRegex(ValueError, "Another update"):
                updater.start(package()[0])
            updater.lock.release()

    def test_listener_must_stop_before_source_replaced(self):
        with tempfile.TemporaryDirectory() as root:
            updater = self.setup_updater(root)
            original = (updater.app / "app.yaml").read_bytes()
            version, files = helper.unpack(package()[0])
            files["app.yaml"] = b"should never be installed"
            updater.status = {"state": "running", "messages": []}
            updater.lock.acquire()
            with patch.object(helper, "verify_controller"), patch.object(updater, "run", return_value=0), \
                    patch.object(updater, "supervisor_active", return_value=False), \
                    patch.object(updater, "wait_stopped", side_effect=RuntimeError("still listening")):
                updater.deploy(version, files)
            self.assertEqual(updater.status["state"], "failed")
            self.assertEqual((updater.app / "app.yaml").read_bytes(), original)
            self.assertFalse(updater.state.exists())

    def test_http_requires_auth_and_does_not_redirect(self):
        with tempfile.TemporaryDirectory() as root:
            updater = self.setup_updater(root)
            server = helper.create_server(updater, "a" * 64, 0)
            thread = threading.Thread(target=server.serve_forever)
            thread.start()
            try:
                url = "http://127.0.0.1:" + str(server.server_port)
                with self.assertRaises(urllib.error.HTTPError) as error:
                    urllib.request.urlopen(url + "/status")
                self.assertEqual(error.exception.code, 401)
                request = urllib.request.Request(url + "/status", headers={"Authorization": "Bearer " + "a" * 64})
                with urllib.request.urlopen(request) as response:
                    self.assertEqual(json.load(response)["state"], "idle")
            finally:
                server.shutdown()
                thread.join()
                server.server_close()


if __name__ == "__main__":
    unittest.main()
