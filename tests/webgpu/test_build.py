"""Check supported build configurations and pinned runtime staging without compiling Godot."""

import argparse
import hashlib
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts import webgpu


class WebGpuBuildTests(unittest.TestCase):
    def test_auto_preserves_unsupported_configurations(self):
        for platform, precision, threads, expected in (
            ("windows", "single", False, True),
            ("linuxbsd", "single", False, True),
            ("macos", "single", False, True),
            ("web", "single", False, True),
            ("web", "single", True, False),
            ("windows", "double", False, False),
            ("web", "double", False, False),
            ("android", "single", False, False),
        ):
            with self.subTest(platform=platform, precision=precision, threads=threads):
                args = argparse.Namespace(webgpu="auto", precision=precision, threads=threads)
                self.assertEqual(webgpu.enabled(args, platform), expected)
                args.webgpu = "no"
                self.assertFalse(webgpu.enabled(args, platform))
                if not expected:
                    args.webgpu = "yes"
                    with self.assertRaises(ValueError):
                        webgpu.enabled(args, platform)

    def test_download_rejects_wrong_checksum_and_replaces_a_bad_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / "download.tar.gz"
            destination.write_bytes(b"bad cache")
            expected = hashlib.sha256(b"expected").hexdigest()

            def fetch(_url, path):
                Path(path).write_bytes(b"incorrect")

            with patch.object(webgpu.urllib.request, "urlretrieve", side_effect=fetch):
                with self.assertRaisesRegex(ValueError, "Checksum mismatch"):
                    webgpu.download("https://example.test/archive", destination, expected)
            self.assertEqual(destination.read_bytes(), b"bad cache")

            def correct_fetch(_url, path):
                Path(path).write_bytes(b"expected")

            with patch.object(webgpu.urllib.request, "urlretrieve", side_effect=correct_fetch):
                webgpu.download("https://example.test/archive", destination, expected)
            self.assertEqual(destination.read_bytes(), b"expected")

    def test_each_native_rid_stages_dependencies_and_notices(self):
        for platform, arch, rid in (
            ("windows", "x86_64", "win-x64"),
            ("linuxbsd", "x86_64", "linux-x64"),
            ("macos", "arm64", "osx-arm64"),
        ):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                sdk, output = root / "sdk", root / "output"
                runtime = sdk / webgpu.RUNTIME[platform]
                runtime.parent.mkdir(parents=True)
                runtime.write_bytes(b"runtime")
                (sdk / "licenses").mkdir()
                (sdk / "licenses/Dawn.txt").write_bytes(b"notice")
                if platform == "windows":
                    for name in ("dxcompiler.dll", "dxil.dll"):
                        (sdk / "bin" / name).write_bytes(b"compiler")
                webgpu.stage_native(sdk, output, platform, arch)
                self.assertEqual((output / runtime.name).read_bytes(), b"runtime")
                self.assertEqual((output / "webgpu" / rid / runtime.name).read_bytes(), b"runtime")
                self.assertEqual((output / "webgpu" / rid / "licenses/Dawn.txt").read_bytes(), b"notice")
                if platform == "windows":
                    self.assertEqual((output / "webgpu/win-x64/dxcompiler.dll").read_bytes(), b"compiler")


if __name__ == "__main__":
    unittest.main()
