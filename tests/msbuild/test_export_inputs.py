"""Exercise pack invalidation with the real targets and a stub exporter, without Godot or workloads."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]


class ExportInputsTests(unittest.TestCase):
    def setUp(self):
        scratch = tempfile.TemporaryDirectory(prefix="2dog-export-inputs-")
        self.root = Path(scratch.name)
        self.addCleanup(scratch.cleanup)

    def fixture(self, platform):
        root = self.root / platform
        game = root / "game"
        game.mkdir(parents=True)
        (game / "project.godot").write_text("config_version=5\n")
        (game / "export_presets.cfg").write_text(
            'name="Windows Desktop"\nname="Web"\nname="Android"\n')
        target_name = {"desktop": "TwoDogExportDesktopPack", "web": "TwoDogExportGamePack",
                       "android": "TwoDogExportAndroidPack"}[platform]
        targets = []
        sources = [ROOT / "twodog.engine/build/2dog.engine.targets"]
        if platform == "web":
            sources.append(ROOT / "platforms/twodog.browser-wasm/build/2dog.browser-wasm.targets")
        for source in sources:
            tree = ET.parse(source)
            target = tree.find(f"Target[@Name='{target_name}']")
            if target is not None:
                for task in target.findall("Exec"):
                    target.remove(task)
                ET.SubElement(target, "WriteLinesToFile", File="$(ProbePack)",
                              Lines="exported", Overwrite="true")
                ET.SubElement(target, "WriteLinesToFile", File="$(ProbeLog)", Lines="exported")
            path = root / source.name
            tree.write(path, encoding="unicode")
            targets.append(f'<Import Project="{escape(path.as_posix())}"/>')
        project = root / "consumer.proj"
        project.write_text(f'''<Project>
  <PropertyGroup>
    <GodotProjectDir>game</GodotProjectDir>
    <BaseIntermediateOutputPath>obj/</BaseIntermediateOutputPath>
    <IntermediateOutputPath>obj/Debug/</IntermediateOutputPath>
    <PublishDir>out/</PublishDir><TargetName>game</TargetName>
    <RuntimeIdentifier>{'browser-wasm' if platform == 'web' else 'win-x64'}</RuntimeIdentifier>
    <TargetPlatformIdentifier>{'android' if platform == 'android' else ''}</TargetPlatformIdentifier>
    <TwoDogAutoImport>false</TwoDogAutoImport>
    <TwoDogExternalGodotEditor>stub</TwoDogExternalGodotEditor>
    <TwoDogWebVariant>release</TwoDogWebVariant>
    <Configuration>Debug</Configuration>
    <ProbeLog>exports.log</ProbeLog>
  </PropertyGroup>
  {''.join(targets)}
  <PropertyGroup>
    <ProbePack>{'out/game.pck' if platform == 'desktop' else '$(IntermediateOutputPath)twodog/game.pck' if platform == 'android' else '$(_TwoDogWebPackPath)'}</ProbePack>
  </PropertyGroup>
  <Target Name="ResolveReferences"/>
  <Target Name="TwoDogResolveContentCapability"/>
  <Target Name="TwoDogWebExportSideModuleImports"/>
  <Target Name="PrepareProbe" BeforeTargets="{target_name}">
    <MakeDir Directories="$([System.IO.Path]::GetDirectoryName('$(ProbePack)'))"/>
  </Target>
</Project>''')
        return root, game, project, target_name

    def export(self, fixture):
        root, _, project, target = fixture
        result = subprocess.run(["dotnet", "msbuild", str(project), "-nologo", f"-t:{target}"],
                                cwd=root, capture_output=True, text=True, timeout=60)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        return (root / "exports.log").read_text().splitlines()

    def test_resource_deletions_and_old_additions_invalidate_every_export(self):
        for platform in ("desktop", "web", "android"):
            with self.subTest(platform=platform):
                fixture = self.fixture(platform)
                _, game, _, _ = fixture
                asset = game / "removed.txt"
                asset.write_text("old content")
                self.assertEqual(1, len(self.export(fixture)))
                self.assertEqual(1, len(self.export(fixture)))
                asset.unlink()
                self.assertEqual(2, len(self.export(fixture)))
                asset.write_text("restored content")
                os.utime(asset, (1, 1))
                self.assertEqual(3, len(self.export(fixture)))
                self.assertEqual(3, len(self.export(fixture)))
                ignored = game / "host"
                ignored.mkdir()
                (ignored / ".gdignore").touch()
                (ignored / "ignored.txt").write_text("not exported")
                self.assertEqual(3, len(self.export(fixture)))


if __name__ == "__main__":
    unittest.main()
