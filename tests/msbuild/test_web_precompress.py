"""Run the web bundle precompression target on a stub app bundle: format selection, stale copies, gz-only bundles."""

import gzip
import subprocess
import tempfile
import unittest
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
TARGETS = ROOT / "platforms/twodog.browser-wasm/build/2dog.browser-wasm.targets"

# Large enough to clear the 4 KiB precompression floor, except the pack.
FILES = {
    "godot.wasm": 8192,
    "godot.pck": 512,
    "godot.js": 8192,
    "index.html": 8192,
    "_framework/app.wasm": 8192,
    "_framework/icudt.dat": 8192,
    "_framework/dotnet.js": 8192,
}
DROPPED = ("godot.wasm", "godot.pck", "_framework/app.wasm", "_framework/icudt.dat")


class WebPrecompressTests(unittest.TestCase):
    def setUp(self):
        scratch = tempfile.TemporaryDirectory(prefix="2dog-web-precompress-")
        self.addCleanup(scratch.cleanup)
        self.root = Path(scratch.name)
        self.bundle = self.root / "AppBundle"
        for name, size in FILES.items():
            path = self.bundle / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((name.encode() * size)[:size])
        self.project = self.root / "consumer.proj"
        self.project.write_text(f'''<Project>
  <PropertyGroup>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <WasmAppDir>{escape(self.bundle.as_posix())}</WasmAppDir>
  </PropertyGroup>
  <Import Project="{escape(TARGETS.as_posix())}"/>
</Project>''')

    def run_target(self, *properties):
        return subprocess.run(
            ["dotnet", "msbuild", str(self.project), "-nologo", "-t:TwoDogWebPrecompressBundle",
             *(f"-p:{p}" for p in properties)],
            cwd=ROOT, capture_output=True, text=True)

    def build(self, *properties):
        result = self.run_target(*properties)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def existing(self, suffix):
        return {name for name in FILES if (self.bundle / (name + suffix)).exists()}

    def test_defaults_write_both_formats_and_keep_originals(self):
        self.build()
        large = {name for name, size in FILES.items() if size >= 4096}
        self.assertEqual(self.existing(".br"), large)
        self.assertEqual(self.existing(".gz"), large)
        self.assertEqual(self.existing(""), set(FILES))
        self.assertEqual(gzip.decompress((self.bundle / "godot.wasm.gz").read_bytes()),
                         (self.bundle / "godot.wasm").read_bytes())

    def test_deselected_formats_are_removed(self):
        self.build()
        self.build("TwoDogWebPrecompressFormats=br")
        self.assertEqual(self.existing(".gz"), set())
        self.assertIn("godot.wasm", self.existing(".br"))
        self.build("TwoDogWebPrecompress=false")
        self.assertEqual(self.existing(".br"), set())
        self.assertEqual(self.existing(""), set(FILES))

    def test_gz_only_bundle_drops_the_inflated_originals(self):
        self.build("TwoDogWebPrecompressFormats=gz", "TwoDogWebPrecompressKeepOriginals=false")
        self.assertEqual(self.existing(".br"), set())
        self.assertEqual(self.existing(""), set(FILES) - set(DROPPED))
        # The pack is below the size floor but has no other way to be served.
        for name in DROPPED:
            self.assertEqual(gzip.decompress((self.bundle / (name + ".gz")).read_bytes()),
                             (name.encode() * FILES[name])[:FILES[name]], name)

    def test_invalid_configurations_fail_without_touching_the_bundle(self):
        for properties, message in (
            (("TwoDogWebPrecompressFormats=zstd",), "unknown TwoDogWebPrecompressFormats entry 'zstd'"),
            (("TwoDogWebPrecompressFormats=br", "TwoDogWebPrecompressKeepOriginals=false"), "needs gzip copies"),
            (("TwoDogWebPrecompress=false", "TwoDogWebPrecompressKeepOriginals=false"), "needs gzip copies"),
        ):
            with self.subTest(properties=properties):
                result = self.run_target(*properties)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(message, result.stdout)
                self.assertEqual(self.existing(""), set(FILES))
                self.assertEqual(self.existing(".gz"), set())


if __name__ == "__main__":
    unittest.main()
