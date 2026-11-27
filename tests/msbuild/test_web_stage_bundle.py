"""Run the WebAssembly SDK bundle staging on a stub publish: the web root and shell land in the bundle, then finalize."""

import subprocess
import tempfile
import unittest
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
TARGETS = ROOT / "platforms/twodog.browser-wasm/build/2dog.browser-wasm.targets"


class WebStageBundleTests(unittest.TestCase):
    def setUp(self):
        scratch = tempfile.TemporaryDirectory(prefix="2dog-web-stage-")
        self.addCleanup(scratch.cleanup)
        self.root = Path(scratch.name)
        self.bundle = self.root / "AppBundle"
        files = {
            "publish/wwwroot/index.html": b"<html></html>",
            "publish/wwwroot/_framework/dotnet.native.wasm": b"runtime",
            "publish/wwwroot/_framework/dotnet.js": b"loader",
            "native/libgodot/libgodot.a": b"archive",
            "native/shell/godot.js": b"const config = { renderingDriver: '', };",
            "native/shell/godot.audio.worklet.js": b"worklet",
            "native/shell/godot.audio.position.worklet.js": b"worklet",
            "AppBundle/stale.txt": b"from an earlier publish",
        }
        for name, data in files.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        self.project = self.root / "consumer.proj"

    def publish(self, sdk_properties):
        self.project.write_text(f'''<Project>
  <PropertyGroup>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <TargetFrameworkVersion>v10.0</TargetFrameworkVersion>
    <PublishDir>{escape((self.root / "publish").as_posix())}/</PublishDir>
    <WasmAppDir>{escape(self.bundle.as_posix())}</WasmAppDir>
    <TwoDogWebNativeDir>{escape((self.root / "native").as_posix())}/</TwoDogWebNativeDir>
    <TwoDogWebSizeManifest>false</TwoDogWebSizeManifest>
    <TwoDogWebPrecompress>false</TwoDogWebPrecompress>
    {sdk_properties}
  </PropertyGroup>
  <Target Name="Publish"/>
  <Import Project="{escape(TARGETS.as_posix())}"/>
</Project>''')
        result = subprocess.run(["dotnet", "msbuild", str(self.project), "-nologo", "-t:Publish"],
                                cwd=ROOT, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_webassembly_sdk_publish_becomes_the_bundle(self):
        self.publish("<UsingMicrosoftNETSdkWebAssembly>true</UsingMicrosoftNETSdkWebAssembly>")
        present = {p.relative_to(self.bundle).as_posix() for p in self.bundle.rglob("*") if p.is_file()}
        self.assertEqual(present, {
            "index.html", "godot.wasm", "_framework/dotnet.js",
            "godot.js", "godot.audio.worklet.js", "godot.audio.position.worklet.js",
        })
        self.assertEqual((self.bundle / "godot.wasm").read_bytes(), b"runtime")
        self.assertIn("renderingDriver: 'opengl3',", (self.bundle / "godot.js").read_text())

    def test_plain_sdk_publish_leaves_the_bundle_to_the_appbundle_builder(self):
        self.publish("")
        present = {p.relative_to(self.bundle).as_posix() for p in self.bundle.rglob("*") if p.is_file()}
        self.assertEqual(present, {"stale.txt"})


if __name__ == "__main__":
    unittest.main()
