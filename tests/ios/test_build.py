"""build-godot.py's iOS platform: archive naming, staging and the command lines it would run, without SCons."""
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

REPO = Path(__file__).resolve().parents[2]


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, file)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


build = module("build_godot_ios_tests", REPO / "build-godot.py")


class IosBuild(unittest.TestCase):
    def test_ios_defaults_to_arm64_on_an_x64_host_and_has_no_editor(self):
        with patch.object(build, "detect_arch", return_value=build.Arch.X86_64):
            config = build.get_platform_config("ios", "auto")
        self.assertEqual("arm64", config.godot_arch)
        self.assertEqual("", config.godot_exe)
        self.assertEqual(".a", config.lib_extension)

    def test_other_architectures_and_the_editor_target_are_rejected(self):
        with self.assertRaises(ValueError):
            build.get_platform_config("ios", "x86_64")
        args = argparse.Namespace(target="editor", simulator=False)
        with patch.object(build, "run_with_live_output") as run:
            with self.assertRaises(ValueError):
                build.build_libgodot(args, build.get_platform_config("ios", "arm64"))
            run.assert_not_called()

    def test_invalid_ios_arguments_are_cli_usage_errors(self):
        for arguments, message in ((["--platform", "ios", "--arch", "x86_64"], "iOS .NET hosts support --arch arm64 only"),
                                   (["--platform", "android", "--simulator"], "--simulator requires --platform ios")):
            with self.subTest(arguments=arguments):
                result = subprocess.run([sys.executable, str(REPO / "build-godot.py"), *arguments],
                                        capture_output=True, text=True, timeout=30)
                self.assertEqual(2, result.returncode)
                self.assertIn("usage:", result.stderr)
                self.assertIn(message, result.stderr)
                self.assertNotIn("Traceback", result.stdout + result.stderr)

    def test_cross_target_configuration_skips_editor_and_glue(self):
        output = io.StringIO()
        with patch.object(sys, "argv", ["build-godot.py", "--platform", "ios", "--no-library"]), \
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

    def test_archive_names_follow_scons(self):
        self.assertEqual("libgodot.ios.template_release.arm64.a", build.ios_archive_name("template_release", False, "single", False))
        self.assertEqual("libgodot.ios.template_debug.arm64.simulator.a", build.ios_archive_name("template_debug", False, "single", True))
        self.assertEqual("libgodot.ios.template_debug.dev.double.arm64.a", build.ios_archive_name("template_debug", True, "double", False))

    def test_build_all_stages_both_variants_per_slice_as_a_static_library(self):
        for simulator, rid in ((False, "ios-arm64"), (True, "iossimulator-arm64")):
            with self.subTest(rid=rid), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                (root / "godot/bin").mkdir(parents=True)
                for target in ("template_release", "template_debug"):
                    name = build.ios_archive_name(target, False, "single", simulator)
                    (root / "godot/bin" / name).write_bytes(f"{rid}/{target}".encode())
                args = argparse.Namespace(target="all", dev_build="no", scu_build="yes", debug_symbols="no",
                                          cache_path="", simulator=simulator, precision="single")
                previous = Path.cwd()
                try:
                    os.chdir(root)
                    with patch.object(build, "run_with_live_output") as run:
                        build.build_libgodot(args, build.get_platform_config("ios", "arm64"))
                    commands = [call.args[0] for call in run.call_args_list]
                    self.assertEqual(2, len(commands))
                    for command in commands:
                        self.assertIn("platform=ios", command)
                        self.assertIn("library_type=static_library", command)
                        self.assertIn("vulkan=no", command)
                        self.assertIn(f"simulator={'yes' if simulator else 'no'}", command)
                        self.assertFalse(any(part.startswith("extra_suffix=") for part in command))
                    self.assertFalse(any("target=editor" in c for c in commands))
                    for target in ("template_release", "template_debug"):
                        staged = root / "godot/bin/ios" / target / rid / "libgodot.a"
                        self.assertEqual(f"{rid}/{target}".encode(), staged.read_bytes())
                finally:
                    os.chdir(previous)

    def test_double_precision_stages_under_the_double_tree(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "godot/bin").mkdir(parents=True)
            (root / "godot/bin" / build.ios_archive_name("template_release", False, "double", False)).write_bytes(b"double")
            args = argparse.Namespace(target="template_release", dev_build="no", scu_build="yes", debug_symbols="no",
                                      cache_path="", simulator=False, precision="double")
            previous = Path.cwd()
            try:
                os.chdir(root)
                with patch.object(build, "run_with_live_output"):
                    build.build_libgodot(args, build.get_platform_config("ios", "arm64", precision="double"))
                self.assertEqual(b"double", (root / "godot/bin/double/ios/template_release/ios-arm64/libgodot.a").read_bytes())
            finally:
                os.chdir(previous)


if __name__ == "__main__":
    unittest.main()
