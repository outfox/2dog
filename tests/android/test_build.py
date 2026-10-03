import argparse
import importlib.util
import io
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
    import sys
    spec = importlib.util.spec_from_file_location(name, file)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


build = module("build_godot_android_tests", REPO / "build-godot.py")
java = module("build_android_java_tests", REPO / "scripts/build_android_java.py")
publish = module("publish_android_tests", REPO / "scripts/publish_android.py")


class AndroidBuild(unittest.TestCase):
    def test_android_defaults_to_device_arm64_on_x64_host(self):
        with patch.object(build, "detect_arch", return_value=build.Arch.X86_64):
            config = build.get_platform_config("android", "auto")
        self.assertEqual("arm64", config.godot_arch)
        self.assertEqual("", config.godot_exe)

    def test_architecture_and_editor_rejected(self):
        with self.assertRaises(ValueError):
            build.get_platform_config("android", "wasm32")
        args = argparse.Namespace(target="editor")
        with patch.object(build, "run_with_live_output") as run:
            with self.assertRaises(ValueError):
                build.build_libgodot(args, build.get_platform_config("android", "arm64"))
            run.assert_not_called()

    def test_invalid_android_architecture_is_cli_usage_error(self):
        result = subprocess.run(
            [sys.executable, str(REPO / "build-godot.py"), "--platform", "android", "--arch", "wasm32"],
            capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(2, result.returncode)
        self.assertIn("usage:", result.stderr)
        self.assertIn("Android .NET hosts support --arch arm64 or x86_64 only", result.stderr)
        self.assertNotIn("Traceback", result.stdout + result.stderr)

    def test_cross_target_configuration_reports_skipped_editor_and_glue(self):
        for target in ("android", "web"):
            with self.subTest(platform=target):
                output = io.StringIO()
                with patch.object(sys, "argv", ["build-godot.py", "--platform", target, "--no-library"]), \
                     patch.object(build, "console", build.Console(file=output, width=120, color_system=None)), \
                     patch.object(build, "build_editor") as editor, \
                     patch.object(build, "generate_glue") as glue, \
                     patch.object(build, "build_libgodot") as library:
                    build.main()
                self.assertRegex(output.getvalue(), r"Skip Editor Build\s+│ Yes")
                self.assertRegex(output.getvalue(), r"Skip Glue Generation\s+│ Yes")
                editor.assert_not_called()
                glue.assert_not_called()
                library.assert_not_called()

    def test_build_all_stages_two_variants_and_cpp_runtime(self):
        for arch, abi in (("arm64", "arm64-v8a"), ("x86_64", "x86_64")):
            with self.subTest(arch=arch), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                for variant in ("debug", "release"):
                    source = root / "godot/platform/android/java/lib/libs" / variant / abi
                    source.mkdir(parents=True)
                    for name in ("libgodot_android.so", "libc++_shared.so"):
                        (source / name).write_bytes(f"{variant}/{arch}/{name}".encode())
                args = argparse.Namespace(target="all", dev_build="yes", scu_build="yes",
                                          debug_symbols="no", cache_path="")
                previous = Path.cwd()
                try:
                    os.chdir(root)
                    with patch.object(build, "run_with_live_output") as run:
                        build.build_libgodot(args, build.get_platform_config("android", arch))
                    commands = [call.args[0] for call in run.call_args_list]
                    self.assertEqual(2, len(commands))
                    self.assertIn("module_mono_enabled=yes", commands[0])
                    self.assertIn("library_type=shared_library", commands[0])
                    self.assertIn("dev_build=no", commands[0])
                    self.assertFalse(any("target=editor" in c for c in commands))
                    for target, variant in (("template_debug", "debug"), ("template_release", "release")):
                        for name in ("libgodot_android.so", "libc++_shared.so"):
                            self.assertEqual(f"{variant}/{arch}/{name}".encode(),
                                             (root / "godot/bin/android" / target / arch / name).read_bytes())
                finally:
                    os.chdir(previous)

    def test_java_payload_preserves_resources_and_excludes_every_native(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory) / "source.aar", Path(directory) / "java/godot.aar"
            entries = {"classes.jar": b"java", "AndroidManifest.xml": b"manifest", "res/xml/provider.xml": b"resource",
                       "jni/arm64-v8a/libgodot_android.so": b"arm", "jni/x86_64/libc++_shared.so": b"cpp"}
            with zipfile.ZipFile(source, "w") as archive:
                for name, data in entries.items():
                    archive.writestr(name, data)
            java.strip_native_libraries(source, output)
            with zipfile.ZipFile(output) as archive:
                self.assertEqual({name for name in entries if not name.startswith("jni/")}, set(archive.namelist()))
                self.assertEqual(b"resource", archive.read("res/xml/provider.xml"))

    def test_gradle_cannot_overwrite_mono_natives(self):
        command = java.gradle_command(Path("godot/platform/android/java"), "release")
        self.assertIn(":lib:assembleTemplateRelease", command)
        self.assertIn(":lib:compileGodotNativeLibsTemplateReleaseArm64", command)
        self.assertIn(":lib:compileGodotNativeLibsTemplateDebugArm64", command)

    def test_publish_uses_absolute_pack_before_apk_build(self):
        args = argparse.Namespace(project=Path("host.csproj"), output=Path("artifact output"), pack=Path("game pack.pck"),
                                  rid="android-x64", configuration="Debug", variant=None, format="apk", property=[])
        command = publish.publish_command(args)
        self.assertIn(f"-p:TwoDogAndroidPack={args.pack.resolve()}", command)
        self.assertIn("-p:TwoDogVariant=debug", command)
        self.assertIn("-p:PublishTrimmed=false", command)
        self.assertIn("-p:RunAOTCompilation=false", command)

    def test_publish_without_pack_leaves_the_export_to_the_build(self):
        args = argparse.Namespace(project=Path("host.csproj"), output=Path("out"), pack=None, rid="android-arm64",
                                  configuration="Release", variant=None, format="apk", property=[])
        self.assertFalse([part for part in publish.publish_command(args) if "TwoDogAndroidPack" in part])


if __name__ == "__main__":
    unittest.main()
