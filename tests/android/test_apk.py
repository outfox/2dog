import importlib.util
import argparse
import contextlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile


REPO = Path(__file__).resolve().parents[2]


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, file)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


inspect = module("inspect_android_apk", REPO / "scripts/inspect_android_apk.py")
build = module("build_android_apk_tests", REPO / "scripts/build_android_apk.py")


class AndroidApkTests(unittest.TestCase):
    def fixture(self, root, rid="android-x64", wrong_variant=False, extra_abi=False, missing_asset=False,
                game="android-smoke-game", libraries=(), wrong_library=False):
        abi, machine = inspect.RID_ABI[rid]
        native = root / "native"
        native.mkdir(exist_ok=True)
        pack = root / "game.pck"
        pack.write_bytes(b"exported game")
        apk = root / "game.apk"
        with zipfile.ZipFile(apk, "w") as archive:
            for asset in ("AndroidManifest.xml", "classes.dex", "assets/game.pck", f"assets/2dog/{game}.dll"):
                if missing_asset and asset == f"assets/2dog/{game}.dll":
                    continue
                archive.writestr(asset, pack.read_bytes() if asset.endswith("game.pck") else b"fixture")
            for name in ("libgodot_android.so", "libc++_shared.so"):
                data = b"\x7fELF\x02\x01" + bytes(12) + machine.to_bytes(2, "little") + b"variant"
                (native / name).write_bytes(data)
                archive.writestr(f"lib/{abi}/{name}", data + (b"wrong" if wrong_variant else b""))
            for name in libraries:
                # A library built for another ABI carries the other machine type.
                other = 183 if machine == 62 else 62
                header = b"\x7fELF\x02\x01" + bytes(12) + (other if wrong_library else machine).to_bytes(2, "little")
                archive.writestr(f"lib/{abi}/{name}", header + b"extra library")
            if extra_abi:
                archive.writestr("lib/unselected/libgodot_android.so", b"wrong ABI")
        return apk, rid, native, pack

    def test_valid_apk_records_selected_native_hashes_for_both_abis(self):
        for rid in inspect.RID_ABI:
            with self.subTest(rid=rid), tempfile.TemporaryDirectory() as directory:
                report = inspect.inspect_apk(*self.fixture(Path(directory), rid))
                self.assertEqual(rid, report["rid"])
                self.assertEqual({"libgodot_android.so", "libc++_shared.so"}, set(report["native_sha256"]))

    def test_wrong_native_variant_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "differs from the selected native variant"):
                inspect.inspect_apk(*self.fixture(Path(directory), wrong_variant=True))

    def test_unselected_abi_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "Expected only ABI"):
                inspect.inspect_apk(*self.fixture(Path(directory), extra_abi=True))

    def test_missing_game_assembly_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "missing assets/2dog/android-smoke-game.dll"):
                inspect.inspect_apk(*self.fixture(Path(directory), missing_asset=True))

    def test_named_game_assembly_and_extra_libraries_are_required(self):
        probe = "libtwodog_probe.android.x86_64.so"
        with tempfile.TemporaryDirectory() as directory:
            args = self.fixture(Path(directory), game="showcase", libraries=(probe,))
            self.assertEqual("android-x64", inspect.inspect_apk(*args, "showcase", [probe])["rid"])
            with self.assertRaisesRegex(ValueError, "missing assets/2dog/android-smoke-game.dll"):
                inspect.inspect_apk(*args)
        with tempfile.TemporaryDirectory() as directory:
            args = self.fixture(Path(directory), game="showcase")
            with self.assertRaisesRegex(ValueError, f"missing lib/x86_64/{probe}"):
                inspect.inspect_apk(*args, "showcase", [probe])
        with tempfile.TemporaryDirectory() as directory:
            args = self.fixture(Path(directory), game="showcase", libraries=(probe,), wrong_library=True)
            with self.assertRaisesRegex(ValueError, f"lib/x86_64/{probe} is not a 64-bit Android x86_64 ELF"):
                inspect.inspect_apk(*args, "showcase", [probe])

    def build_showcase(self, root, *arguments, variants=("debug", "release"), java=True):
        """Runs the APK build with recorded commands; stages only the given natives (and the AARs if java)."""
        for variant in variants:
            if java:
                aar = root / f"godot/bin/android/java/{variant}/godot.aar"
                aar.parent.mkdir(parents=True)
                aar.touch()
            natives = root / f"godot/bin/android/template_{variant}/x86_64"
            natives.mkdir(parents=True)
            for library in ("libgodot_android.so", "libc++_shared.so"):
                (natives / library).touch()
        commands = []
        def run(command):
            command = list(map(str, command))
            commands.append(command)
            if "publish" in command:
                apk = Path(command[command.index("-o") + 1]) / "dev.twodog.showcase-Signed.apk"
                apk.parent.mkdir(parents=True)
                apk.touch()
        with patch.object(build, "REPO", root), patch.object(build, "run", side_effect=run), \
             patch.object(build, "inspect_apk", return_value={}) as inspect_apk, \
             patch.object(sys, "argv", ["build_android_apk.py", "--app", "showcase", *arguments]):
            build.main()
        return commands, inspect_apk

    def test_showcase_app_is_one_publish_that_exports_its_own_pack(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "editor").touch()
            commands, inspect_apk = self.build_showcase(root, "--skip-native", "--skip-java",
                                                        "--editor", str(root / "editor"))
            app = build.APPS["showcase"]
            output = root / "artifacts/android-showcase-apk"
            # Only packing and the publish: the publish builds the game and exports the pck itself.
            self.assertEqual(["publish"], [command[1] for command in commands if command[1] != "pack"])
            publish = commands[-1]
            self.assertEqual(str(app.host), publish[2])
            self.assertIn(f"-p:TwoDogAndroidExportPath={output / 'game.pck'}", publish)
            self.assertIn(f"-p:GodotEditor={(root / 'editor').resolve()}", publish)
            self.assertEqual(output / "game.pck", inspect_apk.call_args.args[3])
            self.assertEqual(("showcase", ["libtwodog_probe.android.x86_64.so"]), inspect_apk.call_args.args[4:])
            report = json.loads((output / "apk-report.json").read_text())
            self.assertEqual("showcase", report["app"])

    def test_without_editor_the_export_uses_the_packaged_editor(self):
        with tempfile.TemporaryDirectory() as directory:
            commands, _ = self.build_showcase(Path(directory), "--skip-native", "--skip-java")
            self.assertFalse([part for part in commands[-1] if part.startswith("-p:GodotEditor=")])

    def test_replaced_pack_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            args = self.fixture(Path(directory))
            args[-1].write_bytes(b"different game")
            with self.assertRaisesRegex(ValueError, "game.pck differs"):
                inspect.inspect_apk(*args)

    def test_apk_build_does_not_rewrite_repository_nuget_configuration(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            original = b"<configuration><!-- user's feed settings --></configuration>"
            (root / "nuget.config").write_bytes(original)
            def fail(command):
                self.assertEqual(original, (root / "nuget.config").read_bytes())
                raise subprocess.CalledProcessError(1, command)
            with patch.object(build, "REPO", root), \
                 patch.object(build, "run", side_effect=fail), \
                 patch.object(sys, "argv", ["build_android_apk.py", "--editor", str(root / "editor"),
                                          "--output", str(root / "output")]):
                (root / "editor").touch()
                with self.assertRaises(SystemExit) as raised:
                    build.main()
            self.assertEqual(1, raised.exception.code)
            self.assertEqual(original, (root / "nuget.config").read_bytes())

    def test_skip_java_rejects_either_missing_variant_before_building(self):
        for missing in ("debug", "release"):
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                present = "release" if missing == "debug" else "debug"
                aar = root / f"godot/bin/android/java/{present}/godot.aar"
                aar.parent.mkdir(parents=True)
                aar.touch()
                with patch.object(build, "REPO", root), patch.object(build, "run") as run:
                    with self.assertRaisesRegex(ValueError, f"Missing {missing} Android Java payload"):
                        build.build(argparse.Namespace(skip_java=True, feed=None))
                run.assert_not_called()

    def test_missing_build_tool_reports_a_concise_error_and_cleans_restore_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "editor").touch()
            error = io.StringIO()
            with patch.object(build, "REPO", root), \
                 patch.object(build, "run", side_effect=FileNotFoundError("missing tool")), \
                 patch.object(sys, "argv", ["build_android_apk.py", "--editor", str(root / "editor"),
                                          "--output", str(root / "output")]), \
                 contextlib.redirect_stderr(error):
                with self.assertRaises(SystemExit) as raised:
                    build.main()
            self.assertEqual(1, raised.exception.code)
            self.assertIn("Android APK build failed: missing tool", error.getvalue())
            self.assertNotIn("Traceback", error.getvalue())
            self.assertEqual([], list((root / "output").glob("restore-*")))


if __name__ == "__main__":
    unittest.main()
