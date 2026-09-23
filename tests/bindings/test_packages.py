"""Integration tests against packed bindings; no native engine or window is started.

Run after packing both managed packages and 2dog.engine:
    python tests/bindings/test_packages.py [--feed packages] [--version VERSION]
"""
import argparse
import platform
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument("--feed", type=Path, default=REPO / "packages")
parser.add_argument("--version")
args, remaining = parser.parse_known_args()
if not args.version:
    props = ET.parse(REPO / "Directory.Build.props")
    args.version = props.findtext(".//GodotVersion") + "." + props.findtext(".//TwoDogRevision")
FEED = args.feed.resolve()
VERSION = args.version
GODOT = ET.parse(REPO / "Directory.Build.props").findtext(".//GodotVersion")
CORE = "2dog.godotsharp"
EDITOR = CORE + ".editor"


def xml(value):
    return str(value).replace("&", "&amp;").replace('"', "&quot;")


class BindingPackages(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="2dog-bindings-")
        cls.root = Path(cls.temp.name)
        shutil.copyfile(REPO / "global.json", cls.root / "global.json")
        cls.local = cls.root / "feed"
        cls.local.mkdir()
        for pkg in (CORE, EDITOR):
            shutil.copyfile(FEED / f"{pkg}.{VERSION}.nupkg", cls.local / f"{pkg}.{VERSION}.nupkg")
        with zipfile.ZipFile(cls.local / f"{CORE}.{VERSION}.nupkg") as z:
            cls.core_bytes = z.read("lib/net10.0/GodotSharp.dll")
        with zipfile.ZipFile(cls.local / f"{EDITOR}.{VERSION}.nupkg") as z:
            cls.editor_bytes = z.read("lib/net10.0/GodotSharpEditor.dll")
        # A newer binding revision with identical bytes exercises exact version guards independently of hashes.
        with zipfile.ZipFile(cls.local / f"{CORE}.{VERSION}.nupkg") as original:
            with zipfile.ZipFile(cls.local / f"{CORE}.99.0.0-test.nupkg", "w") as newer:
                for entry in original.namelist():
                    data = original.read(entry)
                    if entry.endswith(".nuspec"):
                        data = data.replace(f"<version>{VERSION}</version>".encode(), b"<version>99.0.0-test</version>")
                    newer.writestr(entry, data)
        # The test feed contains stock-named packages without relying on nuget.org's Godot release.
        for name, data in (("GodotSharp", cls.core_bytes), ("GodotSharpEditor", cls.editor_bytes)):
            with zipfile.ZipFile(cls.local / f"{name}.0.0.1-test.nupkg", "w") as z:
                z.writestr(f"{name}.nuspec", f'<package><metadata><id>{name}</id><version>0.0.1-test</version>'
                           '<authors>Test</authors><description>Stock identity fixture</description></metadata></package>')
                z.writestr(f"lib/net10.0/{name}.dll", data)
        (cls.root / "Directory.Build.props").write_text("<Project />")
        (cls.root / "Directory.Build.targets").write_text("<Project />")
        (cls.root / "NuGet.Config").write_text(
            '<configuration><packageSources><clear/>'
            f'<add key="test" value="{xml(cls.local)}"/>'
            f'<add key="packages" value="{xml(FEED)}"/>'
            f'<add key="sdk" value="{xml(REPO / "godot/bin/GodotSharp/Tools/nupkgs")}"/>'
            '<add key="nuget" value="https://api.nuget.org/v3/index.json"/>'
            '</packageSources></configuration>')

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def setUp(self):
        self.dir = self.root / self._testMethodName
        self.dir.mkdir()

    def dotnet(self, *command, error=None):
        result = subprocess.run(["dotnet", *map(str, command), "--nologo"], cwd=self.dir,
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180)
        # Preserve diagnostics outside the temporary fixtures on failure.
        if (error is None and result.returncode) or (error and (not result.returncode or error not in result.stdout)):
            self.fail(f"dotnet {' '.join(map(str, command))}\n{result.stdout}")
        return result.stdout

    def project(self, name="Consumer", packages=None, extra="", sdk="Microsoft.NET.Sdk", source=None):
        directory = self.dir / name
        directory.mkdir()
        packages = packages if packages is not None else [(CORE, VERSION)]
        references = "".join(f'<PackageReference Include="{p}" Version="{v}"/>' for p, v in packages)
        csproj = directory / f"{name}.csproj"
        csproj.write_text(f'<Project Sdk="{sdk}"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                          '<DisableImplicitGodotSharpReferences>true</DisableImplicitGodotSharpReferences>'
                          '<DisableImplicitGodotGeneratorReferences>true</DisableImplicitGodotGeneratorReferences>'
                          '<WarningsAsErrors>IL2125;CS8785;CS0433</WarningsAsErrors>'
                          '</PropertyGroup><ItemGroup>' + references + '</ItemGroup>' + extra + '</Project>')
        (directory / "Code.cs").write_text(source or "public class Api { public Godot.Vector2 Value; }")
        return csproj

    def test_package_payload_and_dependencies(self):
        for pkg, assembly in ((CORE, "GodotSharp"), (EDITOR, "GodotSharpEditor")):
            with zipfile.ZipFile(self.local / f"{pkg}.{VERSION}.nupkg") as z:
                self.assertIn(f"lib/net10.0/{assembly}.dll", z.namelist())
                spec = ET.fromstring(z.read(f"{pkg}.nuspec"))
                deps = {e.attrib["id"]: e.attrib["version"] for e in spec.iter()
                        if e.tag.split("}")[-1] == "dependency"}
                self.assertEqual({} if pkg == CORE else {CORE: f"[{VERSION}]"}, deps)
                self.assertFalse(any("/runtimes/" in n or "libgodot" in n for n in z.namelist()))

    def test_game_library_host_build_and_publish(self):
        library = self.project("Library", extra="<PropertyGroup><IsTrimmable>true</IsTrimmable>"
                               "<VerifyReferenceTrimCompatibility>true</VerifyReferenceTrimCompatibility></PropertyGroup>")
        # Use the actual scaffolded game project, so its first restore and generator setup are exercised.
        game_dir = self.dir / "Game"
        game_dir.mkdir()
        template = (REPO / "templates/twodog/Company.Product1.csproj").read_text(encoding="utf-8-sig")
        template = template.replace("GODOT_SDK_VERSION", GODOT).replace("TPLRAWNAME", "Game")
        template = template.replace("$(TwoDogVersion)", VERSION).replace("</Project>",
            '<ItemGroup><ProjectReference Include="../Library/Library.csproj"/></ItemGroup>'
            '<PropertyGroup><WarningsAsErrors>IL2125;CS8785;CS0433</WarningsAsErrors>'
            '<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles></PropertyGroup></Project>')
        game = game_dir / "Game.csproj"
        game.write_text(template)
        (game_dir / "Example.cs").write_text("public partial class Example : Godot.Node { public Api Value = new(); }")
        host = self.project("Host", packages=[], extra='<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>'
                            '<ItemGroup><ProjectReference Include="../Game/Game.csproj"/></ItemGroup>',
                            source="System.Console.WriteLine(typeof(Example).Name);")
        for config in ("Debug", "Release", "Editor"):
            with self.subTest(config=config):
                output = self.dotnet("build", host, "-c", config)
                self.assertNotIn("IL2125", output)
                assets = (game_dir / ".godot/mono/temp/obj/project.assets.json").read_text()
                self.assertNotIn('"GodotSharp/', assets)
                self.assertNotIn('"GodotSharpEditor/', assets)
                self.assertNotIn('"Godot.SourceGenerators/', assets)
                self.assertTrue(list((game_dir / ".godot").rglob("*ScriptPath.generated.cs")))
                publish = self.dir / ("publish-" + config)
                self.dotnet("publish", host, "-c", config, "--no-build", "-o", publish)
                self.assertEqual(self.core_bytes, (publish / "GodotSharp.dll").read_bytes())
                if config in ("Debug", "Editor"):
                    self.assertEqual(self.editor_bytes, (publish / "GodotSharpEditor.dll").read_bytes())
                else:
                    self.assertFalse((publish / "GodotSharpEditor.dll").exists())

    def test_trimmed_publish_accepts_original_binding_inputs(self):
        rid_os = {"Windows": "win", "Linux": "linux", "Darwin": "osx"}[platform.system()]
        rid_arch = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
        project = self.project(extra="<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>",
                               source="System.Console.WriteLine(typeof(Godot.Vector2).Name);")
        publish = self.dir / "trimmed"
        self.dotnet("publish", project, "-c", "Release", "-r", f"{rid_os}-{rid_arch}",
                    "--self-contained", "true", "-p:PublishTrimmed=true", "-o", publish)
        self.assertTrue((publish / "GodotSharp.dll").exists())
        # ILLink is allowed to rewrite the validated binding payload.
        self.assertNotEqual(self.core_bytes, (publish / "GodotSharp.dll").read_bytes())

    def test_stock_core_package_rejected(self):
        project = self.project(packages=[(CORE, VERSION), ("GodotSharp", "0.0.1-test")])
        self.dotnet("build", project, error="TDG001")

    def test_stock_editor_package_rejected(self):
        project = self.project(packages=[(CORE, VERSION), ("GodotSharpEditor", "0.0.1-test")])
        self.dotnet("build", project, error="TDG001")

    def inject(self, assembly, phase):
        bad_dir = self.dir / "stale"
        bad_dir.mkdir()
        original = self.core_bytes if assembly == "GodotSharp" else self.editor_bytes
        bad = bad_dir / (assembly + ".dll")
        # Same loadable PE and assembly version, different contents.
        bad.write_bytes(original + b"stale-copy")
        item = "ReferencePath" if phase == "Compile" else "ResolvedFileToPublish"
        return (f'<Target Name="InjectStaleBinding" BeforeTargets="TwoDogValidate{phase}Bindings">'
                f'<ItemGroup><{item} Include="{xml(bad)}" /></ItemGroup></Target>')

    def test_stale_compile_core_rejected(self):
        project = self.project(extra=self.inject("GodotSharp", "Compile"))
        self.dotnet("build", project, error="TDG003")

    def test_stale_compile_editor_rejected(self):
        project = self.project(packages=[(EDITOR, VERSION)], extra=self.inject("GodotSharpEditor", "Compile"))
        self.dotnet("build", project, error="TDG003")

    def test_editor_without_editor_package_rejected(self):
        project = self.project(extra=self.inject("GodotSharpEditor", "Compile"))
        self.dotnet("build", project, error="TDG002")

    def test_stale_publish_binding_rejected_without_build(self):
        project = self.project()
        self.dotnet("build", project, "-c", "Release")
        project.write_text(project.read_text().replace("</Project>", self.inject("GodotSharp", "Publish") + "</Project>"))
        self.dotnet("publish", project, "-c", "Release", "--no-build", error="TDG003")

    def test_editor_core_version_mismatch_rejected(self):
        project = self.project(packages=[(CORE, "99.0.0-test"), (EDITOR, VERSION)])
        self.dotnet("build", project, error="TDG004")

    def test_engine_package_build_publish_and_version_guard(self):
        self.assertTrue((FEED / f"2dog.engine.{VERSION}.nupkg").exists(), "Pack 2dog.engine before running these tests")
        project = self.project(packages=[("2dog.engine", VERSION), (EDITOR, VERSION)],
                               extra="<PropertyGroup><TwoDogAutoImport>false</TwoDogAutoImport>"
                               "<TwoDogExportPack>false</TwoDogExportPack></PropertyGroup>")
        self.dotnet("build", project, "-c", "Release")
        publish = self.dir / "published"
        self.dotnet("publish", project, "-c", "Release", "--no-build", "-o", publish)
        self.assertEqual(self.core_bytes, (publish / "GodotSharp.dll").read_bytes())
        self.assertEqual(self.editor_bytes, (publish / "GodotSharpEditor.dll").read_bytes())
        project.write_text(project.read_text().replace("</Project>",
            f'<ItemGroup><PackageReference Include="{CORE}" Version="99.0.0-test"/></ItemGroup></Project>'))
        # Remove the editor dependency so this failure must come from the engine's version guard.
        project.write_text(project.read_text().replace(f'<PackageReference Include="{EDITOR}" Version="{VERSION}"/>', ""))
        self.dotnet("build", project, "-c", "Release", error="TDG004")

    def test_equal_copy_allowed(self):
        copy = self.dir / "copy"
        copy.mkdir()
        (copy / "GodotSharp.dll").write_bytes(self.core_bytes)
        extra = ('<Target Name="UseCopiedBinding" BeforeTargets="TwoDogValidateCompileBindings">'
                 f'<ItemGroup><ReferencePath Include="{xml(copy / "GodotSharp.dll")}"/></ItemGroup></Target>')
        project = self.project(extra=extra)
        self.dotnet("build", project)


if __name__ == "__main__":
    unittest.main(argv=[__file__, *remaining], verbosity=2)
