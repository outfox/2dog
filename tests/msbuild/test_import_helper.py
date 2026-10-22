"""Check helper discovery and import command arguments without loading a native engine."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]


class ImportHelperDiscoveryTests(unittest.TestCase):
    def test_source_helpers_follow_precision_and_do_not_fall_back_to_single(self):
        with tempfile.TemporaryDirectory(prefix="2dog-helper-discovery-") as scratch:
            root = Path(scratch)
            targets = root / "twodog.engine/build/2dog.engine.targets"
            targets.parent.mkdir(parents=True)
            shutil.copyfile(ROOT / "twodog.engine/build/2dog.engine.targets", targets)
            project = root / "consumer.proj"
            for subdir in ("", "double/"):
                for config in ("Debug", "Release"):
                    helper = root / f"twodog.import/bin/{subdir}{config}/net10.0/2dog.import.dll"
                    helper.parent.mkdir(parents=True)
                    helper.write_bytes(b"fixture")

            def resolve(subdir):
                project.write_text('<Project><PropertyGroup>'
                    f'<TwoDogNativeOutputSubdir>{subdir}</TwoDogNativeOutputSubdir>'
                    f'</PropertyGroup><Import Project="{escape(str(targets))}"/></Project>')
                result = subprocess.run(["dotnet", "msbuild", str(project), "-nologo",
                    "-getProperty:TwoDogImportHelperPath"], capture_output=True, text=True, timeout=60)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                return result.stdout.strip()

            for subdir in ("", "double/"):
                self.assertEqual((root / f"twodog.import/bin/{subdir}Release/net10.0/2dog.import.dll").resolve(),
                                 Path(resolve(subdir)).resolve())
            (root / "twodog.import/bin/double/Release/net10.0/2dog.import.dll").unlink()
            self.assertEqual((root / "twodog.import/bin/double/Debug/net10.0/2dog.import.dll").resolve(),
                             Path(resolve("double/")).resolve())
            (root / "twodog.import/bin/double/Debug/net10.0/2dog.import.dll").unlink()
            self.assertEqual("", resolve("double/"))


class ImportProjectPathTests(unittest.TestCase):
    def test_sibling_project_path_reaches_import_process_intact(self):
        with tempfile.TemporaryDirectory(prefix="2dog project path-") as scratch:
            root = Path(scratch).resolve()
            probe = root / "probe"
            probe.mkdir()
            (probe / "probe.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>''')
            (probe / "Program.cs").write_text('''
System.IO.File.WriteAllText("arguments.json", System.Text.Json.JsonSerializer.Serialize(args));
return System.IO.Directory.Exists(args[args.Length - 1]) ? 0 : 1;
''')
            result = subprocess.run(["dotnet", "build", str(probe / "probe.csproj"), "-nologo",
                "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false"],
                capture_output=True, text=True, timeout=60)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            binary = probe / "bin/Debug/net10.0/probe"
            editor = binary.with_suffix(".exe") if os.name == "nt" else binary
            helper = binary.with_suffix(".dll")
            game = root / "Godot game"
            game.mkdir()
            (game / "project.godot").write_text("config_version=5\n")
            solution_dir = root / "MainProject"
            solution_dir.mkdir()
            host = root / "OtherStuff"
            host.mkdir()
            project = host / "consumer.csproj"
            solution = solution_dir / "master.slnx"
            solution.write_text('<Solution><Project Path="../OtherStuff/consumer.csproj"/></Solution>')
            targets = escape((ROOT / "twodog.engine/build/2dog.engine.targets").as_posix())
            for backend in ("helper", "editor"):
                for absolute in (False, True):
                    for separator in ("", "/", "\\") if os.name == "nt" else ("", "/"):
                        with self.subTest(backend=backend, absolute=absolute, separator=separator):
                            path = str(game) if absolute else "../Godot game"
                            project.write_text(f'''<Project DefaultTargets="Build">
  <PropertyGroup>
    <GodotProjectDir>{escape(path + separator)}</GodotProjectDir>
    <GodotEditor>{escape(str(editor)) if backend == 'editor' else ''}</GodotEditor>
    <TwoDogImportHelperPath>{escape(str(helper))}</TwoDogImportHelperPath>
    <TwoDogEditorLibGodotPath>{escape(str(helper))}</TwoDogEditorLibGodotPath>
    <TwoDogGodotToolsDir>{escape(str(probe))}/</TwoDogGodotToolsDir>
    <TwoDogGodotApiSourcePath>{escape(str(probe))}/</TwoDogGodotApiSourcePath>
    <TwoDogForceImport>true</TwoDogForceImport>
    <TwoDogSkipGodotApiCopy>true</TwoDogSkipGodotApiCopy>
  </PropertyGroup>
  <Import Project="{targets}"/>
  <Target Name="Build"/>
</Project>''')
                            result = subprocess.run(["dotnet", "msbuild", str(solution), "-nologo", "-t:Build"], cwd=solution_dir,
                                capture_output=True, text=True, timeout=60)
                            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                            self.assertTrue((host / "arguments.json").is_file(), result.stdout + result.stderr)
                            args = json.loads((host / "arguments.json").read_text())
                            self.assertEqual(4 if backend == "editor" else 7, len(args))
                            self.assertEqual(game, Path(args[-1]).resolve())
                            self.assertTrue((game / ".godot/2dog.import.stamp").is_file())


if __name__ == "__main__":
    unittest.main()
