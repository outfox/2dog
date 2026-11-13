"""Exercise NativeAOT consumer targets without a restore or native engine build."""

from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]


class AotPublishTests(unittest.TestCase):
    def setUp(self):
        scratch_root = ROOT / "temp"
        scratch_root.mkdir(exist_ok=True)
        scratch = tempfile.TemporaryDirectory(prefix="aot publish-", dir=scratch_root)
        self.scratch = Path(scratch.name).resolve()
        self.addCleanup(scratch.cleanup)

    def project(self, properties=""):
        targets = escape((ROOT / "twodog.engine/build/2dog.engine.targets").as_posix())
        project = self.scratch / "consumer.proj"
        project.write_text(f'''<Project>
  <PropertyGroup>
    <PublishAot>true</PublishAot>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <TargetDir>{escape(self.scratch.as_posix())}/host/</TargetDir>
    {properties}
  </PropertyGroup>
  <Import Project="{targets}"/>
  <Target Name="ResolveReferences"/>
  <Target Name="TwoDogResolveContentCapability"/>
</Project>''', encoding="utf-8")
        return project

    def prepare(self, project):
        result = subprocess.run(["dotnet", "msbuild", str(project), "-nologo", "-t:TwoDogPrepareAotPublish"],
                                cwd=ROOT, capture_output=True, text=True, timeout=60)
        output = result.stdout + result.stderr
        self.assertNotEqual(result.returncode, 0, output)
        return output

    def test_editor_variant_is_still_rejected_without_godot_project(self):
        output = self.prepare(self.project("<TwoDogVariant>editor</TwoDogVariant>"))
        self.assertIn("PublishAot (NativeAOT) does not support TwoDogVariant 'editor'", output)


if __name__ == "__main__":
    unittest.main()
