"""The iOS app build, inspection and Simulator scripts, with every external tool mocked."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import plistlib
import struct
import subprocess
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "scripts"))


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, file)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


inspect = module("inspect_ios_app", REPO / "scripts/inspect_ios_app.py")
build = module("build_ios_app_tests", REPO / "scripts/build_ios_app.py")
simulator = module("test_ios_simulator_script", REPO / "scripts/test_ios_simulator.py")


def macho(platform=None, cpu_type=inspect.CPU_TYPE_ARM64):
    commands = b""
    if platform is not None:
        commands = struct.pack("<IIIIII", inspect.LC_BUILD_VERSION, 24, platform, 0x000F0000, 0x000F0000, 0)
    header = struct.pack("<IIIIIII", inspect.MACHO_MAGIC_64, cpu_type, 0, 2, 1 if platform is not None else 0, len(commands), 0)
    return header + b"\0" * 4 + commands


class InspectIosApp(unittest.TestCase):
    def fixture(self, root, rid="iossimulator-arm64", platform=inspect.PLATFORM_IOS_SIMULATOR, game="ios-smoke-game",
                missing=(), wrong_pack=False, legacy_assembly=False):
        app = root / "smoke.app"
        app.mkdir()
        pack = root / "game.pck"
        pack.write_bytes(b"exported game")
        with (app / "Info.plist").open("wb") as stream:
            plistlib.dump({"CFBundleExecutable": "smoke", "CFBundleIdentifier": "dev.twodog.smoke"}, stream)
        (app / "smoke").write_bytes(macho(platform))
        (app / "game.pck").write_bytes(b"other" if wrong_pack else pack.read_bytes())
        assembly = app / ".monotouch-64" / f"{game}.dll" if legacy_assembly else app / f"{game}.dll"
        assembly.parent.mkdir(exist_ok=True)
        assembly.write_bytes(b"assembly")
        for name in missing:
            (app / name).unlink()
        return app, rid, pack

    def test_valid_bundle_reports_platform_and_pack_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            app, rid, pack = self.fixture(Path(directory))
            report = inspect.inspect_app(app, rid, pack)
            self.assertEqual("dev.twodog.smoke", report["bundle_id"])
            self.assertEqual([inspect.PLATFORM_IOS_SIMULATOR], report["platforms"])
            self.assertFalse(report["signed"])

    def test_legacy_assembly_layout_is_accepted(self):
        with tempfile.TemporaryDirectory() as directory:
            app, rid, pack = self.fixture(Path(directory), legacy_assembly=True)
            inspect.inspect_app(app, rid, pack)

    def test_wrong_slice_pack_or_missing_files_are_rejected(self):
        cases = (dict(rid="ios-arm64"), dict(wrong_pack=True), dict(missing=("game.pck",)),
                 dict(missing=("ios-smoke-game.dll",)), dict(missing=("smoke",)))
        for case in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory() as directory:
                app, rid, pack = self.fixture(Path(directory), **case)
                with self.assertRaises(ValueError):
                    inspect.inspect_app(app, rid, pack)

    def test_images_without_build_version_pass_and_foreign_images_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            app, rid, pack = self.fixture(Path(directory), platform=None)
            self.assertEqual([], inspect.inspect_app(app, rid, pack)["platforms"])
            (app / "smoke").write_bytes(macho(cpu_type=0x01000007))
            with self.assertRaises(ValueError):
                inspect.inspect_app(app, rid, pack)
            (app / "smoke").write_bytes(b"#!/bin/sh")
            with self.assertRaises(ValueError):
                inspect.inspect_app(app, rid, pack)


class BuildIosApp(unittest.TestCase):
    def build(self, root, rid, configuration="Debug", feed=True, **extra):
        output = root / "output"
        (output / "app").mkdir(parents=True, exist_ok=True)
        (output / "app/smoke.app").mkdir(exist_ok=True)
        defaults = dict(app="smoke", rid=rid, configuration=configuration, precision="single",
                        feed=root if feed else None, dotnet="dotnet", editor=None, codesign_key=None,
                        codesign_provision=None, skip_native=True, cache_path=None)
        args = SimpleNamespace(**{**defaults, **extra})
        with patch.object(build, "REPO", root), patch.object(build, "run") as run, \
                patch.object(build, "inspect_app", return_value={}) as inspected:
            build.build_with_packages(args, output, root, root / "config", root / "cache")
        return run, inspected

    def test_simulator_uses_build_and_device_uses_a_signed_publish(self):
        with tempfile.TemporaryDirectory() as directory:
            run, inspected = self.build(Path(directory), "iossimulator-arm64")
            command = run.call_args.args[0]
            self.assertEqual("build", command[1])
            self.assertIn("-r", command)
            self.assertIn("iossimulator-arm64", command)
            self.assertFalse(any(str(part).startswith("-p:CodesignKey") for part in command))
            self.assertEqual("iossimulator-arm64", inspected.call_args.args[1])
        with tempfile.TemporaryDirectory() as directory:
            run, _ = self.build(Path(directory), "ios-arm64", "Release", codesign_key="Apple Development",
                                codesign_provision="2dog Dev")
            command = run.call_args.args[0]
            self.assertEqual("publish", command[1])
            self.assertIn("-p:CodesignKey=Apple Development", command)
            self.assertIn("-p:CodesignProvision=2dog Dev", command)

    def test_device_builds_without_a_signing_identity_are_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "codesign-key"):
                self.build(Path(directory), "ios-arm64")

    def test_maui_build_selects_ios_without_requiring_the_android_workload(self):
        with tempfile.TemporaryDirectory() as directory:
            run, inspected = self.build(Path(directory), "iossimulator-arm64", app="maui")
            command = run.call_args.args[0]
            self.assertIn("net10.0-ios", command)
            self.assertIn("-p:TwoDogMauiAndroid=false", command)
            self.assertEqual("showcase", inspected.call_args.args[3])

    def test_private_feed_packs_both_slices_and_the_host_glue(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for configuration in ("Debug", "Release"):
                for rid in build.RIDS:
                    native = root / f"godot/bin/ios/template_{configuration.lower()}/{rid}"
                    native.mkdir(parents=True)
                    (native / "libgodot.a").write_bytes(b"archive")
            args = SimpleNamespace(skip_native=False, cache_path=None, precision="single", dotnet="dotnet")
            with patch.object(build, "REPO", root), patch.object(build, "run") as run:
                build.pack_local_feed(args, root / "feed", ["-p:X=1"])
            commands = [[str(part) for part in call.args[0]] for call in run.call_args_list]
            native_builds = [c for c in commands if "build-godot.py" in c[1]]
            self.assertEqual(2, len(native_builds))
            self.assertEqual(1, sum("--simulator" in c for c in native_builds))
            packed = [c[2] for c in commands if c[1] == "pack"]
            self.assertTrue(any(p.endswith("twodog.ios") for p in packed), packed)
            self.assertTrue(any(p.endswith("platforms") for p in packed), packed)

    def test_restore_config_lists_the_private_feed_first(self):
        with tempfile.TemporaryDirectory() as directory:
            config = Path(directory) / "NuGet.Config"
            build.restore_config(config, Path(directory) / "feed")
            text = config.read_text()
            self.assertIn('key="ios-packages"', text)
            self.assertIn("nuget.org", text)
            self.assertLess(text.index("ios-packages"), text.index("nuget.org"))


class SimulatorScript(unittest.TestCase):
    def run_script(self, lines, timeout=5, marker="2DOG_IOS_CSHARP_SMOKE_PASSED"):
        calls = []
        output = io.StringIO()

        class Launch:
            def __init__(self):
                self.stdout = io.StringIO("".join(lines))
                self.killed = False

            def kill(self):
                self.killed = True

        launch = Launch()

        def run(command, **kwargs):
            calls.append((command, kwargs))
            return SimpleNamespace(returncode=0)

        with patch.object(sys, "argv", ["test_ios_simulator.py", "smoke.app", "--timeout", str(timeout), "--marker", marker]), \
                patch.object(simulator.subprocess, "run", side_effect=run), \
                patch.object(simulator.subprocess, "Popen", return_value=launch), \
                contextlib.redirect_stdout(output):
            try:
                simulator.main()
                exit_message = None
            except SystemExit as error:
                exit_message = str(error)
        return calls, output.getvalue(), exit_message, launch

    def test_marker_on_the_console_passes_and_terminates_the_app(self):
        calls, output, exit_message, launch = self.run_script(["booting\n", "2DOG_IOS_CSHARP_SMOKE_PASSED\n", "more\n"])
        self.assertIsNone(exit_message)
        self.assertEqual("2DOG_IOS_CSHARP_SMOKE_PASSED\n", output)
        self.assertEqual(["xcrun", "simctl", "install", "booted"], calls[0][0][:4])
        self.assertEqual(5, calls[0][1]["timeout"])
        self.assertEqual("terminate", calls[-1][0][2])
        self.assertTrue(launch.killed)

    def test_missing_marker_fails_with_the_console_output(self):
        _, output, exit_message, _ = self.run_script(["booting\n", "2DOG_IOS_CSHARP_SMOKE_FAILED: boom\n"])
        self.assertEqual("iOS smoke marker not observed before timeout", exit_message)
        self.assertIn("2DOG_IOS_CSHARP_SMOKE_FAILED", output)

    def test_hung_install_is_bounded_and_does_not_launch_the_app(self):
        with patch.object(sys, "argv", ["test_ios_simulator.py", "smoke.app", "--timeout", "1"]), \
                patch.object(simulator.subprocess, "run", side_effect=subprocess.TimeoutExpired("simctl install", 1)) as run, \
                patch.object(simulator.subprocess, "Popen") as launch:
            with self.assertRaisesRegex(SystemExit, "app installation timed out"):
                simulator.main()
        self.assertEqual(1, run.call_args.kwargs["timeout"])
        launch.assert_not_called()

    def test_silent_process_cannot_block_the_smoke_timeout(self):
        launch = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"],
                                  stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        started = time.monotonic()
        try:
            with patch.object(sys, "argv", ["test_ios_simulator.py", "smoke.app", "--timeout", "1"]), \
                    patch.object(simulator.subprocess, "run"), \
                    patch.object(simulator.subprocess, "Popen", return_value=launch), \
                    contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(SystemExit, "marker not observed"):
                    simulator.main()
            self.assertLess(time.monotonic() - started, 5)
        finally:
            launch.kill()
            launch.wait(timeout=5)
            launch.stdout.close()

    def test_empty_marker_and_zero_timeout_are_usage_errors(self):
        for arguments in (["--marker", ""], ["--timeout", "0"]):
            with self.subTest(arguments=arguments):
                result = subprocess.run([sys.executable, str(REPO / "scripts/test_ios_simulator.py"), "smoke.app", *arguments],
                                        capture_output=True, text=True, timeout=30)
                self.assertEqual(2, result.returncode)
                self.assertIn("usage:", result.stderr)


if __name__ == "__main__":
    unittest.main()
