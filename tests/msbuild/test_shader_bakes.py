"""The bake list is an incremental export input, including an explicit opt-out."""
import subprocess
import tempfile
import unittest
from pathlib import Path
from xml.sax.saxutils import escape

TARGETS = Path(__file__).resolve().parents[2] / "twodog.engine/build/2dog.engine.targets"


class ShaderBakeTests(unittest.TestCase):
    def test_bake_list_is_validated_and_tracked(self):
        with tempfile.TemporaryDirectory() as scratch:
            project = Path(scratch, "host.proj")
            project.write_text(f"""<Project>
  <PropertyGroup>
    <TwoDogGodotProjectFullPath>$(MSBuildProjectDirectory)</TwoDogGodotProjectFullPath>
    <TwoDogImportHelperPath>$(MSBuildProjectFullPath)</TwoDogImportHelperPath>
    <IntermediateOutputPath>obj/</IntermediateOutputPath>
    <TwoDogBakeShaders>forward_plus;mobile</TwoDogBakeShaders>
  </PropertyGroup>
  <Import Project="{escape(TARGETS.as_posix())}"/>
  <Target Name="ResolveReferences"/>
</Project>""", encoding="utf-8")
            command = ["dotnet", "msbuild", str(project), "-nologo", "-t:TwoDogPrepareExport"]
            stamp = Path(scratch, "obj/twodog-export-inputs.list")
            for value, expected in [(None, "forward_plus,mobile"), ("mobile", "mobile"), ("none", "none")]:
                args = command if value is None else [*command, f"-p:TwoDogBakeShaders={value}"]
                result = subprocess.run(args, cwd=scratch, capture_output=True, text=True)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn(f"shaders={expected}", stamp.read_text(encoding="utf-8"))
            result = subprocess.run([*command, "-p:TwoDogBakeShaders=vulkan"], cwd=scratch, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("TwoDogBakeShaders accepts", result.stdout)


if __name__ == "__main__":
    unittest.main()
