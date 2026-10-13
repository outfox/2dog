"""Exercise CI's native release layout without requiring native toolchains."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("stage_natives", ROOT / "scripts/stage_natives_release.py")
staging = importlib.util.module_from_spec(spec)
spec.loader.exec_module(staging)


class NativeReleaseStagingTests(unittest.TestCase):
    def release(self, assets, precision):
        suffix = ".double" if precision == "double" else ""
        files = {
            "godotsharp": ["Api/Release/GodotSharp.dll", "Api/Debug/GodotPlugins.dll", "Tools/GodotTools.dll"],
            "godotsharp-nupkgs": ["Godot.NET.Sdk.4.7.2.nupkg"],
            "godot-source-generators": ["Godot.SourceGenerators.dll"],
            "godot-editor-linux-x64": [f"godot.linuxbsd.editor{suffix}.x86_64.executable.mono"],
            "godot-android-java": ["debug/godot.aar", "release/godot.aar"],
        }
        for rid, platform, arch, prefix, ext in (
            ("linux-x64", "linuxbsd", "x86_64", "libgodot", "so"),
            ("win-x64", "windows", "x86_64", "godot", "dll"),
            ("osx-arm64", "macos", "arm64", "libgodot", "dylib"),
        ):
            for target in ("template_release", "template_debug", "editor"):
                files[f"libgodot-{rid}-{target}"] = [f"{prefix}.{platform}.{target}{suffix}.{arch}.shared_library.{ext}"]
        for target in ("template_release", "template_debug"):
            files[f"libgodot-browser-wasm-{target}"] = ["libgodot/libgodot.a"]
            for rid in ("android-arm64", "android-x64"):
                files[f"libgodot-{rid}-{target}"] = ["libgodot_android.so", "libc++_shared.so"]
        assets.mkdir()
        for name, members in files.items():
            with zipfile.ZipFile(assets / f"{name}.zip", "w") as archive:
                for member in members:
                    archive.writestr("./" + member, precision.encode())

    def test_stages_every_platform_and_keeps_java_and_sdk_neutral(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            for precision in ("single", "double"):
                assets = root / precision
                self.release(assets, precision)
                staging.stage(assets, root, precision)
            for subdir, precision in (("", "single"), ("double", "double")):
                selected = root / "godot/bin" / subdir
                self.assertEqual(precision.encode(), (selected / "GodotSharp/Api/Release/GodotSharp.dll").read_bytes())
                self.assertEqual(precision.encode(), (selected / "web/template_release/libgodot/libgodot.a").read_bytes())
                self.assertEqual(precision.encode(), (selected / "android/template_debug/arm64/libgodot_android.so").read_bytes())
            self.assertTrue((root / "godot/bin/android/java/debug/godot.aar").is_file())
            self.assertTrue((root / "godot/bin/GodotSharp/Tools/nupkgs/Godot.NET.Sdk.4.7.2.nupkg").is_file())

    def test_single_release_cannot_satisfy_a_double_request(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            self.release(root / "assets", "single")
            with self.assertRaisesRegex(ValueError, "double"):
                staging.stage(root / "assets", root, "double")
            self.assertFalse((root / "godot/bin").exists())

    def test_incomplete_release_is_rejected_before_staging(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            self.release(root / "assets", "double")
            (root / "assets/libgodot-android-arm64-template_debug.zip").unlink()
            with self.assertRaises(FileNotFoundError):
                staging.stage(root / "assets", root, "double")
            self.assertFalse((root / "godot/bin").exists())


if __name__ == "__main__":
    unittest.main()
