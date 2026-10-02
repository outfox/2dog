"""Pack real Android NuGets and exercise their targets without the Android workload or device."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[2]


def xml(value):
    return str(value).replace("&", "&amp;").replace('"', "&quot;")


class AndroidPackages(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="2dog-android-packages-")
        cls.root = Path(cls.temp.name)
        cls.bin = cls.root / "natives"
        cls.feed = cls.root / "feed"
        props = ET.parse(REPO / "Directory.Build.props")
        cls.version = props.findtext(".//GodotVersion") + "." + props.findtext(".//NativesRevision")
        cls.packages = cls.root / "cache"
        for arch in ("arm64", "x86_64"):
            for target in ("template_debug", "template_release"):
                directory = cls.bin / "android" / target / arch
                directory.mkdir(parents=True)
                for name in ("libgodot_android.so", "libc++_shared.so"):
                    (directory / name).write_bytes(f"{arch}/{target}/{name}".encode())
        java = cls.bin / "android/java"
        for variant in ("debug", "release"):
            directory = java / variant
            directory.mkdir(parents=True)
            with zipfile.ZipFile(directory / "godot.aar", "w") as archive:
                archive.writestr("classes.jar", variant.encode())
                archive.writestr("AndroidManifest.xml", b"manifest")
        for rid in ("android-arm64", "android-x64", "android"):
            command = ["dotnet", "pack", str(REPO / f"platforms/twodog.{rid}/twodog.{rid}.csproj"),
                       "--nologo", f"-p:GodotBinDir={cls.bin.as_posix()}/",
                       f"-p:AndroidJavaPayloadDir={java.as_posix()}/", f"-p:PackageOutputPath={cls.feed}",
                       "-p:NuGetAudit=false"]
            if os.environ.get("TWODOG_TEST_RESTORE_PACKAGES"):
                command.append(f"-p:RestorePackagesPath={os.environ['TWODOG_TEST_RESTORE_PACKAGES']}")
            result = subprocess.run(command, cwd=REPO, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180)
            if result.returncode:
                raise RuntimeError(f"{' '.join(command)}\n{result.stdout}")
        for package in cls.feed.glob("*.nupkg"):
            with zipfile.ZipFile(package) as archive:
                # NuGet global cache layout, consumed exactly as installed package targets would be.
                stem = package.name.removesuffix(f".{cls.version}.nupkg")
                archive.extractall(cls.packages / stem / cls.version)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def consumer(self, rid="android-arm64", variant="release", platform="android", missing=False, multi=False):
        directory = Path(tempfile.mkdtemp(dir=self.root))
        imports = []
        for architecture in ("android-arm64", "android-x64"):
            imports.append(f'<Import Project="{xml(self.packages / f"2dog.{architecture}" / self.version / "build" / f"2dog.{architecture}.targets")}"/>')
        project = directory / "Consumer.proj"
        cache = directory / "empty-cache" if missing else self.packages
        project.write_text(f'''<Project>
<PropertyGroup><TargetPlatformIdentifier>{platform}</TargetPlatformIdentifier>
<RuntimeIdentifier>{'' if multi else rid}</RuntimeIdentifier><RuntimeIdentifiers>{rid if multi else ''}</RuntimeIdentifiers>
<TwoDogVariant>{variant}</TwoDogVariant><NuGetPackageRoot>{xml(cache.as_posix())}/</NuGetPackageRoot>
<TwoDogLocalGodotBin>{xml((directory / 'absent').as_posix())}/</TwoDogLocalGodotBin></PropertyGroup>
{''.join(imports)}
<Target Name="Collect" DependsOnTargets="TwoDogResolveAndroidNatives">
<WriteLinesToFile File="{xml(directory / 'items.txt')}" Lines="@(AndroidNativeLibrary->'%(Abi)|%(FullPath)')" Overwrite="true"/>
</Target></Project>''')
        result = subprocess.run(["dotnet", "msbuild", str(project), "-t:Collect", "-nologo"], cwd=REPO,
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
        items = (directory / "items.txt").read_text().splitlines() if (directory / "items.txt").exists() else []
        return result, items

    def test_variant_packages_have_jni_name_and_cpp_runtime(self):
        for rid in ("android-arm64", "android-x64"):
            for variant in ("debug", "release"):
                with zipfile.ZipFile(self.feed / f"2dog.{rid}.{variant}.{self.version}.nupkg") as archive:
                    natives = {name for name in archive.namelist() if name.startswith("runtimes/")}
                    self.assertEqual({f"runtimes/{rid}/native/libgodot_android.so", f"runtimes/{rid}/native/libc++_shared.so"}, natives)

    def test_java_package_has_two_native_free_flavors_and_host(self):
        with zipfile.ZipFile(self.feed / f"2dog.android.{self.version}.nupkg") as archive:
            for variant in ("debug", "release"):
                self.assertIn(f"android/java/{variant}/godot.aar", archive.namelist())
            self.assertIn("android/host/TwoDogActivity.java", archive.namelist())
            self.assertIn("buildTransitive/2dog.android.targets", archive.namelist())

    def test_meta_dependencies_pin_both_variants_and_exclude_native_flow(self):
        for rid in ("android-arm64", "android-x64"):
            with zipfile.ZipFile(self.feed / f"2dog.{rid}.{self.version}.nupkg") as archive:
                nuspec = ET.fromstring(archive.read(f"2dog.{rid}.nuspec"))
                deps = nuspec.findall(".//{*}dependency")
                self.assertEqual(2, len(deps))
                for dep in deps:
                    self.assertEqual(f"[{self.version}]", dep.attrib["version"])
                    self.assertEqual("native", dep.attrib["exclude"])

    def test_selected_abi_and_variant_only(self):
        for rid, abi in (("android-arm64", "arm64-v8a"), ("android-x64", "x86_64")):
            for variant in ("debug", "release"):
                result, items = self.consumer(rid, variant)
                self.assertEqual(0, result.returncode, result.stdout)
                self.assertEqual(2, len(items), result.stdout)
                self.assertTrue(all(item.startswith(abi + "|") and f"2dog.{rid}.{variant}" in item for item in items))

    def test_multiple_rids_package_both_abis(self):
        result, items = self.consumer("android-arm64;android-x64", multi=True)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual(4, len(items))

    def test_desktop_consumer_gets_no_android_natives(self):
        result, items = self.consumer("win-x64", platform="")
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual([], items)

    def test_editor_unknown_rid_and_missing_payload_fail_loudly(self):
        for arguments, diagnostic in (({"variant": "editor"}, "TDGA001"), ({"rid": "android-x86"}, "TDGA002"),
                                      ({"missing": True}, "TDGA003")):
            result, _ = self.consumer(**arguments)
            self.assertNotEqual(0, result.returncode)
            self.assertIn(diagnostic, result.stdout)

    def test_force_pack_cannot_publish_empty_android_packages(self):
        for rid in ("android-arm64", "android-x64", "android"):
            project = REPO / f"platforms/twodog.{rid}/twodog.{rid}.csproj"
            result = subprocess.run(["dotnet", "msbuild", str(project), "-getProperty:IsPackable", "-nologo",
                                     "-p:ForcePackAllPlatforms=true", f"-p:GodotBinDir={self.root.as_posix()}/absent/",
                                     f"-p:AndroidJavaPayloadDir={self.root.as_posix()}/absent/"], cwd=REPO,
                                    text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertEqual("false", result.stdout.strip())

    def test_game_dll_is_added_before_android_asset_paths_are_computed(self):
        directory = Path(tempfile.mkdtemp(dir=self.root))
        game = directory / "smoke-game.dll"
        game.write_bytes(b"managed game")
        pack = directory / "game.pck"
        pack.write_bytes(b"game content")
        targets = self.packages / "2dog.android" / self.version / "build/2dog.android.targets"
        project = directory / "Host.proj"
        project.write_text(f'''<Project>
<PropertyGroup><TargetPlatformIdentifier>android</TargetPlatformIdentifier><TwoDogVariant>debug</TwoDogVariant>
<TwoDogAndroidGameAssembly>smoke-game</TwoDogAndroidGameAssembly><TwoDogAndroidPack>{xml(pack)}</TwoDogAndroidPack></PropertyGroup>
<Import Project="{xml(targets)}"/>
<Target Name="ResolveReferences"><ItemGroup><ReferenceCopyLocalPaths Include="{xml(game)}"/></ItemGroup></Target>
<Target Name="_ComputeAndroidAssetsPaths"><WriteLinesToFile File="{xml(directory / 'assets.txt')}"
Lines="@(AndroidAsset->'%(Link)|%(FullPath)')" Overwrite="true"/></Target>
</Project>''')
        result = subprocess.run(["dotnet", "msbuild", str(project), "-t:_ComputeAndroidAssetsPaths", "-nologo"],
                                cwd=REPO, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
        self.assertEqual(0, result.returncode, result.stdout)
        assets = (directory / "assets.txt").read_text().splitlines()
        self.assertEqual(2, len(assets))
        self.assertTrue(any(item.startswith("2dog/smoke-game.dll|") for item in assets))
        self.assertTrue(any(item.startswith("game.pck|") for item in assets))


if __name__ == "__main__":
    unittest.main()
