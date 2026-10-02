import importlib.util
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
    def fixture(self, root, rid="android-x64", wrong_variant=False, extra_abi=False, missing_asset=False):
        abi, machine = inspect.RID_ABI[rid]
        native = root / "native"
        native.mkdir(exist_ok=True)
        pack = root / "game.pck"
        pack.write_bytes(b"exported game")
        apk = root / "game.apk"
        with zipfile.ZipFile(apk, "w") as archive:
            for asset in ("AndroidManifest.xml", "classes.dex", "assets/game.pck",
                          "assets/2dog/android-smoke-game.dll"):
                if missing_asset and asset == "assets/2dog/android-smoke-game.dll":
                    continue
                archive.writestr(asset, pack.read_bytes() if asset.endswith("game.pck") else b"fixture")
            for name in ("libgodot_android.so", "libc++_shared.so"):
                data = b"\x7fELF\x02\x01" + bytes(12) + machine.to_bytes(2, "little") + b"variant"
                (native / name).write_bytes(data)
                archive.writestr(f"lib/{abi}/{name}", data + (b"wrong" if wrong_variant else b""))
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


if __name__ == "__main__":
    unittest.main()
