import contextlib
import importlib.util
import io
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch


REPO = Path(__file__).resolve().parents[2]


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, file)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


device = module("test_android_device_scripts", REPO / "scripts/test_android_device.py")
publish = module("publish_android_script_tests", REPO / "scripts/publish_android.py")


class Clock:
    def __init__(self):
        self.now = 0.0
        self.sleeps = []

    def sleep(self, seconds):
        self.sleeps.append(seconds)
        self.now += seconds


class AndroidDeviceScript(unittest.TestCase):
    def run_smoke(self, clock, timeout, read, expected_timeout=True, arguments=()):
        calls = []
        output = io.StringIO()

        def run(command, **kwargs):
            calls.append((command, kwargs))
            if "pidof" in command or "logcat" in command:
                return read(command, kwargs)
            return SimpleNamespace(stdout="")

        with patch.object(sys, "argv", ["test_android_device.py", "smoke.apk", "--serial", "test-device",
                                       "--timeout", str(timeout), *arguments]), \
                patch.object(device.time, "monotonic", side_effect=lambda: clock.now), \
                patch.object(device.time, "sleep", side_effect=clock.sleep), \
                patch.object(device.subprocess, "run", side_effect=run), contextlib.redirect_stdout(output):
            if expected_timeout:
                with self.assertRaisesRegex(SystemExit, "marker not observed before timeout"):
                    device.main()
            else:
                device.main()
        self.assertEqual(False, calls[-1][1]["check"])
        self.assertEqual(5, calls[-1][1]["timeout"])
        return calls, output.getvalue()

    def test_pidof_timeout_uses_remaining_budget_and_skips_logcat(self):
        clock = Clock()

        def read(command, options):
            self.assertIn("pidof", command)
            self.assertEqual(3, options["timeout"])
            clock.now += options["timeout"]
            raise subprocess.TimeoutExpired(command, options["timeout"])

        calls, _ = self.run_smoke(clock, 3, read)
        self.assertFalse(any("logcat" in command for command, _ in calls))
        self.assertEqual(3, clock.now)
        self.assertEqual([], clock.sleeps)

    def test_logcat_timeout_recomputes_budget_after_pidof(self):
        clock = Clock()

        def read(command, options):
            if "pidof" in command:
                self.assertEqual(5, options["timeout"])
                clock.now += 3
                return SimpleNamespace(stdout="123 456\n")
            self.assertEqual("123", command[-1])
            self.assertEqual(2, options["timeout"])
            clock.now += options["timeout"]
            raise subprocess.TimeoutExpired(command, options["timeout"])

        self.run_smoke(clock, 5, read)
        self.assertEqual(5, clock.now)
        self.assertEqual([], clock.sleeps)

    def test_no_logcat_started_if_pidof_finishes_at_deadline(self):
        clock = Clock()

        def read(command, options):
            self.assertIn("pidof", command)
            clock.now += options["timeout"]
            return SimpleNamespace(stdout="123\n")

        calls, _ = self.run_smoke(clock, 2, read)
        self.assertFalse(any("logcat" in command for command, _ in calls))

    def test_poll_sleep_cannot_extend_deadline(self):
        clock = Clock()

        def read(command, options):
            self.assertIn("pidof", command)
            clock.now += 2.75
            return SimpleNamespace(stdout="")

        self.run_smoke(clock, 3, read)
        self.assertEqual([0.25], clock.sleeps)
        self.assertEqual(3, clock.now)

    def test_slow_poll_can_retry_and_marker_succeeds_with_standard_caps(self):
        clock = Clock()
        pid_calls = 0

        def read(command, options):
            nonlocal pid_calls
            if "pidof" in command:
                pid_calls += 1
                self.assertEqual(10, options["timeout"])
                if pid_calls == 1:
                    clock.now += 10
                    raise subprocess.TimeoutExpired(command, options["timeout"])
                return SimpleNamespace(stdout="123\n")
            self.assertEqual(15, options["timeout"])
            return SimpleNamespace(stdout="2DOG_ANDROID_CSHARP_SMOKE_PASSED\n")

        _, output = self.run_smoke(clock, 40, read, expected_timeout=False)
        self.assertEqual(2, pid_calls)
        self.assertEqual([1], clock.sleeps)
        self.assertEqual("2DOG_ANDROID_CSHARP_SMOKE_PASSED\n", output)

    def test_custom_marker_replaces_the_smoke_marker(self):
        def read(command, options):
            if "pidof" in command:
                return SimpleNamespace(stdout="123\n")
            return SimpleNamespace(stdout="2DOG_ANDROID_CSHARP_SMOKE_PASSED\n2DOG_ANDROID_SHOWCASE_SMOKE_PASSED\n")

        marker = ["--package", "dev.twodog.showcase", "--marker", "2DOG_ANDROID_SHOWCASE_SMOKE_PASSED"]
        calls, output = self.run_smoke(Clock(), 5, read, expected_timeout=False, arguments=marker)
        self.assertEqual("2DOG_ANDROID_SHOWCASE_SMOKE_PASSED\n", output)
        self.assertIn("dev.twodog.showcase", calls[0][0] + calls[1][0])

        def other_app(command, options):
            if "pidof" in command:
                return SimpleNamespace(stdout="123\n")
            return SimpleNamespace(stdout="2DOG_ANDROID_CSHARP_SMOKE_PASSED\n")

        self.run_smoke(Clock(), 2, other_app, arguments=marker)


class AndroidPublishScript(unittest.TestCase):
    def test_format_property_is_reserved_case_insensitively(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / "host.csproj"
            pack = Path(directory) / "game.pck"
            project.write_text("<Project />")
            pack.write_bytes(b"pck")
            for property_name in ("AndroidPackageFormats", "androidpackageformats", "ANDROIDPACKAGEFORMATS"):
                with self.subTest(property_name=property_name), \
                        patch.object(sys, "argv", ["publish_android.py", str(project), "--pack", str(pack),
                                                 "--format", "apk", "--property", f"{property_name}=aab"]), \
                        patch.object(publish.subprocess, "run") as run, \
                        contextlib.redirect_stderr(io.StringIO()) as error:
                    with self.assertRaises(SystemExit) as raised:
                        publish.main()
                    self.assertEqual(2, raised.exception.code)
                    self.assertIn(f"Use the dedicated option for {property_name}", error.getvalue())
                    run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
