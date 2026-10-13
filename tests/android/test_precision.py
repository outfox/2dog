"""The APK runner must inspect and publish the selected precision's payloads."""
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import build_android_apk as apk


class AndroidPrecisionTests(unittest.TestCase):
    def test_publish_and_inspection_select_the_same_precision(self):
        for precision, subdir in (("single", ""), ("double", "double/")):
            with self.subTest(precision=precision), tempfile.TemporaryDirectory() as scratch:
                root = Path(scratch)
                native = root / f"godot/bin/{subdir}android/template_release/x86_64"
                native.mkdir(parents=True)
                for name in ("libgodot_android.so", "libc++_shared.so"):
                    (native / name).write_bytes(precision.encode())
                output = root / "output"
                (output / "apk").mkdir(parents=True)
                signed = output / "apk/app-Signed.apk"
                signed.write_bytes(b"apk")
                args = SimpleNamespace(app="smoke", rid="android-x64", configuration="Release",
                    precision=precision, feed=root, dotnet="dotnet", aot=False, editor=None)
                with patch.object(apk, "REPO", root), patch.object(apk, "run") as run, \
                        patch.object(apk, "inspect_apk", return_value={}) as inspect:
                    apk.build_with_packages(args, output, root, root / "config", root / "cache")
                self.assertIn(f"-p:TwoDogPrecision={precision}", run.call_args.args[0])
                self.assertEqual(native, inspect.call_args.args[2])


if __name__ == "__main__":
    unittest.main()
