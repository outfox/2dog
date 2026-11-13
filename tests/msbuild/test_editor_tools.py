"""Exercise editor tooling deployment without restoring packages or loading Godot."""

from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]


class EditorToolsTests(unittest.TestCase):
    def setUp(self):
        scratch_root = ROOT / "temp"
        scratch_root.mkdir(exist_ok=True)
        scratch = tempfile.TemporaryDirectory(prefix="editor tools-", dir=scratch_root)
        self.scratch = Path(scratch.name).resolve()
        self.addCleanup(scratch.cleanup)
        package = self.scratch / "package"
        self.tools = package / "tools/GodotSharp/Tools"
        self.payload = {
            "GodotTools.dll": b"editor tooling",
            "GodotTools.deps.json": b"{}",
            "GodotTools.runtimeconfig.json": b"{}",
            "GodotTools.ProjectEditor.dll": b"tool dependency",
            "fr/GodotTools.resources.dll": b"localized resources",
        }
        for name, contents in {
            **self.payload,
            "GodotTools.pdb": b"debug symbols",
            "nupkgs/Godot.NET.Sdk.nupkg": b"build-time package feed",
        }.items():
            path = self.tools / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(contents)
        self.tools_targets = package / "build/2dog.tools.targets"
        self.tools_targets.parent.mkdir()
        shutil.copyfile(ROOT / "platforms/twodog.tools/build/2dog.tools.targets", self.tools_targets)
        self.output = self.scratch / "build"
        self.publish = self.scratch / "publish"

    def run_target(self, target, configuration="Editor", *properties):
        engine = escape((ROOT / "twodog.engine/build/2dog.engine.targets").as_posix())
        project = self.scratch / "consumer.proj"
        project.write_text(f'''<Project>
  <PropertyGroup>
    <!-- This fixture tests tools deployment, independently of built GodotPlugins in the checkout. -->
    <TwoDogSkipGodotApiCopy>true</TwoDogSkipGodotApiCopy>
    <OutputPath>{escape(self.output.as_posix())}/</OutputPath>
    <PublishDir>{escape(self.publish.as_posix())}/</PublishDir>
  </PropertyGroup>
  <Import Project="{engine}"/>
  <Import Project="{escape(self.tools_targets.as_posix())}"/>
  <Target Name="Build"/>
  <Target Name="Publish"/>
</Project>''', encoding="utf-8")
        result = subprocess.run(
            ["dotnet", "msbuild", str(project), "-nologo", f"-t:{target}",
             f"-p:Configuration={configuration}", *properties],
            cwd=ROOT, capture_output=True, text=True, timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_runtime_variants_do_not_copy_tools(self):
        for configuration in ("Debug", "Release"):
            with self.subTest(configuration=configuration):
                self.run_target("Build;Publish", configuration)
                self.assertFalse(self.output.exists())
                self.assertFalse(self.publish.exists())
        self.run_target("Build;Publish", "Editor", "-p:TwoDogVariant=debug")
        self.assertFalse(self.output.exists())
        self.assertFalse(self.publish.exists())


if __name__ == "__main__":
    unittest.main()
