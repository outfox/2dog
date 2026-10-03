import importlib.util
import argparse
import contextlib
import io
import json
import os
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
                game="android-smoke-game", libraries=()):
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
                archive.writestr(f"lib/{abi}/{name}", b"extra library")
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

    def build_showcase(self, root, *arguments, variants=("debug", "release"), java=True):
        """Runs the APK build with recorded commands; stages only the given natives (and the AARs if java)."""
        (root / "editor").touch()
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
             patch.object(sys, "argv", ["build_android_apk.py", "--app", "showcase", "--editor",
                                      str(root / "editor"), *arguments]):
            build.main()
        return commands, inspect_apk

    def test_showcase_app_builds_its_game_with_the_android_probe_and_publishes_its_host(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            commands, inspect_apk = self.build_showcase(root, "--skip-native", "--skip-java")
            app = build.APPS["showcase"]
            self.assertIn([str(app.game / "showcase.csproj"), "-c", "Debug", "-p:TwoDogProbeAndroid=true"],
                          [command[2:6] for command in commands if command[1] == "build"])
            self.assertTrue(any("--export-pack" in command and str(app.game) in command for command in commands))
            self.assertTrue(any(command[1:3] == ["publish", str(app.host)] for command in commands))
            self.assertFalse(any("--generate-mono-glue" in command for command in commands))
            self.assertEqual(("showcase", ["libtwodog_probe.android.x86_64.so"]), inspect_apk.call_args.args[4:])
            report = json.loads((root / "artifacts/android-showcase-apk/apk-report.json").read_text())
            self.assertEqual("showcase", report["app"])

    def test_feed_builds_against_packed_packages_without_building_or_packing_payloads(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            feed = root / "packages"
            feed.mkdir()
            # Only the selected variant's natives, for the inspection; no Java payloads.
            commands, _ = self.build_showcase(root, "--feed", str(feed), variants=("debug",), java=False)
            scripts = ("build-godot.py", "build_android_java.py")
            self.assertEqual([], [command for command in commands if command[1] == "pack"
                                  or any(part.endswith(scripts) for part in command)])
            self.assertTrue(any(command[1] == "publish" for command in commands))
            config = (root / "artifacts/android-showcase-apk/NuGet.Config").read_text()
            self.assertIn(f'value="{feed.resolve()}"', config)

    def test_feed_requires_the_selected_natives_for_the_inspection(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "packages").mkdir()
            with self.assertRaises(SystemExit) as raised, contextlib.redirect_stderr(io.StringIO()) as error:
                self.build_showcase(root, "--feed", str(root / "packages"), "--configuration", "Release",
                                    variants=("debug",), java=False)
            self.assertEqual(1, raised.exception.code)
            self.assertIn("template_release", error.getvalue())

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

    def test_private_restore_cache_is_removed_after_success_and_failure(self):
        for fail in (False, True):
            with self.subTest(fail=fail), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                cache_paths = []
                def finish(args, output, feed, config, restore):
                    cache_paths.append(restore)
                    (restore / "cached-package").write_bytes(b"temporary package")
                    if fail:
                        raise subprocess.CalledProcessError(1, "dotnet")
                    apk = output / "game.apk"
                    apk.write_bytes(b"APK artifact")
                    return apk
                args = argparse.Namespace(output=root / "output", skip_java=False, feed=None)
                with patch.object(build, "REPO", root), patch.object(build, "build_with_packages", side_effect=finish):
                    if fail:
                        with self.assertRaises(subprocess.CalledProcessError):
                            build.build(args)
                    else:
                        self.assertTrue(build.build(args).is_file())
                self.assertEqual(1, len(cache_paths))
                self.assertFalse(cache_paths[0].exists())

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

    def test_smoke_host_selects_packages_for_singular_and_plural_rids_and_rejects_unsupported_rids(self):
        cases = [([], {"android-arm64"}, None),
                 (["-p:RuntimeIdentifier=android-x64"], {"android-x64"}, None),
                 (["-p:RuntimeIdentifiers=android-x64"], {"android-x64"}, None),
                 ([], {"android-arm64", "android-x64"}, "android-arm64;android-x64")]
        for properties, expected, rids in cases:
            with self.subTest(properties=properties):
                environment = dict(os.environ)
                environment.pop("RuntimeIdentifier", None)
                environment.pop("RuntimeIdentifiers", None)
                if rids:
                    environment["RuntimeIdentifiers"] = rids
                result = subprocess.run(["dotnet", "msbuild", str(REPO / "tests/android/host/android-smoke.csproj"),
                                         "-p:TargetFramework=net10.0", "-t:TwoDogValidateAndroidSmokeRids",
                                         "-getItem:PackageReference", "-nologo", *properties], cwd=REPO,
                                        env=environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
                self.assertEqual(0, result.returncode, result.stdout)
                references = json.loads(result.stdout)["Items"]["PackageReference"]
                actual = {item["Identity"].removeprefix("2dog.") for item in references
                          if item["Identity"].startswith("2dog.android-")}
                self.assertEqual(expected, actual)
        result = subprocess.run(["dotnet", "msbuild", str(REPO / "tests/android/host/android-smoke.csproj"),
                                 "-p:TargetFramework=net10.0", "-p:RuntimeIdentifier=android-x86",
                                 "-t:TwoDogValidateAndroidSmokeRids", "-nologo"], cwd=REPO,
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIn("TDGSMOKE001", result.stdout)


if __name__ == "__main__":
    unittest.main()
