"""Exercise NativeAOT consumer targets without a restore or native engine build."""

import json
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

    def project(self, properties="", game_reference=False):
        targets = escape((ROOT / "twodog.engine/build/2dog.engine.targets").as_posix())
        game = self.scratch / "game"
        references = ""
        if game_reference:
            assembly = escape((game / "bin/Game.dll").as_posix())
            source = escape((game / "Game.csproj").as_posix())
            references = f'''<ItemGroup><_ResolvedProjectReferencePaths Include="{assembly}">
              <MSBuildSourceProjectFile>{source}</MSBuildSourceProjectFile>
            </_ResolvedProjectReferencePaths></ItemGroup>'''
        project = self.scratch / "consumer.proj"
        project.write_text(f'''<Project>
  <PropertyGroup>
    <PublishAot>true</PublishAot>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <TargetDir>{escape(self.scratch.as_posix())}/host/</TargetDir>
    {properties}
  </PropertyGroup>
  <Import Project="{targets}"/>
  <Target Name="ResolveReferences">{references}</Target>
  <Target Name="TwoDogResolveContentCapability"/>
</Project>''', encoding="utf-8")
        return project

    def prepare(self, project, success=True):
        result = subprocess.run(
            ["dotnet", "msbuild", str(project), "-nologo", "-t:TwoDogPrepareAotPublish",
             "-getItem:TrimmerRootAssembly", "-getProperty:_TwoDogExportAssemblyDir"],
            cwd=ROOT, capture_output=True, text=True, timeout=60,
        )
        output = result.stdout + result.stderr
        self.assertEqual(result.returncode == 0, success, output)
        return json.loads(output) if success else output

    def roots(self, values):
        return [item["Identity"] for item in values["Items"]["TrimmerRootAssembly"]]

    def test_host_without_godot_project_skips_export_resolution(self):
        for properties in ("", "<TwoDogExportPack>false</TwoDogExportPack>"):
            with self.subTest(properties=properties):
                values = self.prepare(self.project(properties))
                self.assertEqual(self.roots(values), ["GodotSharp"])
                self.assertEqual(values["Properties"]["_TwoDogExportAssemblyDir"], "")

    def test_configured_game_reference_is_still_rooted(self):
        values = self.prepare(self.project("<GodotProjectDir>game</GodotProjectDir>", game_reference=True))
        self.assertEqual(self.roots(values), ["GodotSharp", "Game"])
        self.assertEqual(
            Path(values["Properties"]["_TwoDogExportAssemblyDir"]).resolve(),
            self.scratch / "game/bin",
        )

    def test_configured_project_without_reference_keeps_host_output_fallback(self):
        values = self.prepare(self.project("<GodotProjectDir>game</GodotProjectDir>"))
        self.assertEqual(self.roots(values), ["GodotSharp"])
        self.assertEqual(
            Path(values["Properties"]["_TwoDogExportAssemblyDir"]).resolve(),
            self.scratch / "host",
        )

    def test_editor_variant_is_still_rejected_without_godot_project(self):
        output = self.prepare(self.project("<TwoDogVariant>editor</TwoDogVariant>"), success=False)
        self.assertIn("PublishAot (NativeAOT) does not support TwoDogVariant 'editor'", output)


if __name__ == "__main__":
    unittest.main()
