"""Pack the iOS runtime NuGets from stub archives and exercise their targets, plus the 2dog.ios host targets from
source, without the ios workload, a Mac or a device."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[2]
RIDS = ("ios-arm64", "iossimulator-arm64")


def xml(value):
    return str(value).replace("&", "&amp;").replace('"', "&quot;")


def msbuild(project, target, cwd, timeout=60):
    return subprocess.run(["dotnet", "msbuild", str(project), f"-t:{target}", "-nologo"], cwd=cwd,
                          text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)


class IosPackages(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="2dog-ios-packages-")
        cls.root = Path(cls.temp.name)
        cls.bin = cls.root / "natives"
        cls.feed = cls.root / "feed"
        # Packaging fixtures only need NETStandard.Library from nuget.org.
        cls.restore_config = cls.root / "NuGet.Config"
        cls.restore_config.write_text(
            '<configuration><packageSources><clear/>'
            '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>'
            '</packageSources></configuration>')
        cls.restore_packages = os.environ.get("TWODOG_TEST_RESTORE_PACKAGES", str(cls.root / "restore"))
        props = ET.parse(REPO / "Directory.Build.props")
        cls.version = props.findtext(".//GodotVersion") + "." + props.findtext(".//NativesRevision")
        cls.packages = cls.root / "cache"
        for rid in RIDS:
            for target in ("template_debug", "template_release"):
                directory = cls.bin / "ios" / target / rid
                directory.mkdir(parents=True)
                (directory / "libgodot.a").write_bytes(f"{rid}/{target}".encode())
        for rid in RIDS:
            command = ["dotnet", "pack", str(REPO / f"platforms/twodog.{rid}/twodog.{rid}.csproj"),
                       "--nologo", f"-p:GodotBinDir={cls.bin.as_posix()}/", f"-p:PackageOutputPath={cls.feed}",
                       f"-p:RestoreConfigFile={cls.restore_config}", f"-p:RestorePackagesPath={cls.restore_packages}",
                       "-p:NuGetAudit=false"]
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

    def consumer(self, rid="ios-arm64", variant="release", platform="ios", missing=False):
        directory = Path(tempfile.mkdtemp(dir=self.root))
        imports = "".join(
            f'<Import Project="{xml(self.packages / f"2dog.{r}" / self.version / "build" / f"2dog.{r}.targets")}"/>' for r in RIDS)
        project = directory / "Consumer.proj"
        cache = directory / "empty-cache" if missing else self.packages
        project.write_text(f'''<Project>
<PropertyGroup><TargetPlatformIdentifier>{platform}</TargetPlatformIdentifier><RuntimeIdentifier>{rid}</RuntimeIdentifier>
<TwoDogVariant>{variant}</TwoDogVariant><NuGetPackageRoot>{xml(cache.as_posix())}</NuGetPackageRoot>
<TwoDogLocalGodotBin>{xml((directory / 'absent').as_posix())}/</TwoDogLocalGodotBin></PropertyGroup>
{imports}
<Target Name="Collect" DependsOnTargets="TwoDogResolveIosNatives">
<WriteLinesToFile File="{xml(directory / 'items.txt')}" Lines="@(NativeReference->'%(Kind)|%(ForceLoad)|%(LinkWithSwiftSystemLibraries)|%(FullPath)')" Overwrite="true"/>
</Target></Project>''')
        result = msbuild(project, "Collect", REPO)
        items = (directory / "items.txt").read_text().splitlines() if (directory / "items.txt").exists() else []
        return result, items

    def test_variant_packages_carry_one_archive_under_their_rid(self):
        for rid in RIDS:
            for variant in ("debug", "release"):
                with zipfile.ZipFile(self.feed / f"2dog.{rid}.{variant}.{self.version}.nupkg") as archive:
                    natives = {name for name in archive.namelist() if name.startswith("runtimes/")}
                    self.assertEqual({f"runtimes/{rid}/native/libgodot.a"}, natives)

    def test_meta_dependencies_pin_both_variants_and_ship_the_resolver(self):
        for rid in RIDS:
            with zipfile.ZipFile(self.feed / f"2dog.{rid}.{self.version}.nupkg") as archive:
                nuspec = ET.fromstring(archive.read(f"2dog.{rid}.nuspec"))
                deps = {d.get("id"): d.get("version") for d in nuspec.findall(".//{*}dependency")}
                self.assertEqual({f"2dog.{rid}.release": f"[{self.version}]", f"2dog.{rid}.debug": f"[{self.version}]"}, deps)
                names = archive.namelist()
                self.assertIn("build/2dog.ios-native-resolver.targets", names)
                self.assertIn(f"build/2dog.{rid}.targets", names)
                self.assertIn(f"buildTransitive/2dog.{rid}.targets", names)

    def test_resolver_links_the_selected_variant_with_force_load(self):
        for rid in RIDS:
            for variant, target in (("release", "template_release"), ("debug", "template_debug")):
                with self.subTest(rid=rid, variant=variant):
                    result, items = self.consumer(rid, variant)
                    self.assertEqual(0, result.returncode, result.stdout)
                    self.assertEqual(1, len(items), items)
                    kind, force_load, swift, path = items[0].split("|")
                    self.assertEqual(("Static", "True", "True"), (kind, force_load, swift))
                    self.assertEqual(f"{rid}/{target}".encode(), Path(path).read_bytes())

    def test_editor_variant_and_missing_payload_fail_clearly(self):
        result, _ = self.consumer(variant="editor")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("TDGI001", result.stdout)
        result, _ = self.consumer(missing=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("TDGI003", result.stdout)

    def test_unsupported_rid_and_other_platforms_are_inert_or_refused(self):
        result, items = self.consumer(rid="ios-x64")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("TDGI002", result.stdout)
        result, items = self.consumer(platform="android")
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual([], items)


class IosHostTargets(unittest.TestCase):
    """twodog.ios/build/2dog.ios.targets from source: the pack bundle resource, roots and validation."""

    def project(self, root, properties="", pack=None, rid="iossimulator-arm64"):
        targets = REPO / "twodog.ios/build/2dog.ios.targets"
        pack_property = f"<TwoDogIosPack>{xml(pack)}</TwoDogIosPack>" if pack else ""
        project = root / "Host.proj"
        project.write_text(f'''<Project>
<PropertyGroup><TargetPlatformIdentifier>ios</TargetPlatformIdentifier><RuntimeIdentifier>{rid}</RuntimeIdentifier>
<Configuration>Debug</Configuration><TwoDogIosGameAssembly>MyGame</TwoDogIosGameAssembly>{pack_property}{properties}</PropertyGroup>
<Import Project="{xml(targets)}"/>
<Target Name="ResolveReferences"/>
<Target Name="PrepareForBuild"/>
<Target Name="_CollectBundleResources"/>
<Target Name="Collect" DependsOnTargets="PrepareForBuild;_CollectBundleResources">
<WriteLinesToFile File="{xml(root / 'bundle.txt')}" Lines="@(BundleResource->'%(Link)|%(FullPath)')" Overwrite="true"/>
<WriteLinesToFile File="{xml(root / 'roots.txt')}" Lines="@(TrimmerRootAssembly)" Overwrite="true"/>
<WriteLinesToFile File="{xml(root / 'symbols.txt')}" Lines="@(ReferenceNativeSymbol->'%(Identity)|%(SymbolType)')" Overwrite="true"/>
<WriteLinesToFile File="{xml(root / 'natives.txt')}" Lines="@(NativeReference->'%(Identity)|%(Kind)|%(ForceLoad)')" Overwrite="true"/>
<WriteLinesToFile File="{xml(root / 'props.txt')}" Lines="$(TwoDogVariant)|$(TwoDogExportPack)|$(UseInterpreter)" Overwrite="true"/>
</Target></Project>''')
        return project

    def test_pack_becomes_the_bundle_resource_and_the_game_is_rooted(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            pack = root / "exported.pck"
            pack.write_bytes(b"pck")
            result = msbuild(self.project(root, pack=pack), "Collect", REPO)
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertEqual([f"game.pck|{pack}"], (root / "bundle.txt").read_text().splitlines())
            roots = (root / "roots.txt").read_text().split()
            for expected in ("GodotSharp", "twodog", "twodog.ios", "MyGame"):
                self.assertIn(expected, roots)
            # The app links with -dead_strip: 2dog.engine's libgodot import survives only as a named symbol.
            self.assertEqual(["set_load_from_executable_fn|Function"], (root / "symbols.txt").read_text().split())
            self.assertEqual("debug|false|true", (root / "props.txt").read_text().strip())

    @staticmethod
    def gdextension_project(root, xcframework=True, dependency=True):
        """A Godot project with one .gdextension: an xcframework (device and Simulator slices) or a static library."""
        game = root / "game"
        (game / "bin").mkdir(parents=True)
        (game / "project.godot").write_text("")
        if xcframework:
            framework = game / "bin/probe.xcframework"
            for identifier, variant in (("ios-arm64", ""), ("ios-arm64_x86_64-simulator", "simulator")):
                (framework / identifier).mkdir(parents=True)
                (framework / identifier / "libprobe.a").write_bytes(b"!<arch>\n")
            slices = "".join(f'''<dict>
<key>LibraryIdentifier</key><string>{identifier}</string><key>LibraryPath</key><string>libprobe.a</string>
<key>SupportedArchitectures</key><array><string>arm64</string>{extra}</array><key>SupportedPlatform</key><string>ios</string>{variant}
</dict>''' for identifier, extra, variant in (
                ("ios-arm64", "", ""),
                ("ios-arm64_x86_64-simulator", "<string>x86_64</string>", "<key>SupportedPlatformVariant</key><string>simulator</string>")))
            (framework / "Info.plist").write_text(f'''<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>AvailableLibraries</key><array>{slices}</array>
<key>CFBundlePackageType</key><string>XFWK</string><key>XCFrameworkFormatVersion</key><string>1.0</string></dict></plist>''')
            library = "res://bin/probe.xcframework"
        else:
            (game / "bin/libprobe.a").write_bytes(b"!<arch>\n")
            library = "res://bin/libprobe.a"
        (game / "bin/libdep.a").write_bytes(b"!<arch>\n")
        dependencies = '\n[dependencies]\n\nios.arm64 = {"res://bin/libdep.a": ""}\n' if dependency else ""
        (game / "probe.gdextension").write_text(f'''[configuration]

entry_symbol = "probe_init"
compatibility_minimum = "4.7"

[libraries]

macos = "res://bin/libprobe.dylib"
ios.arm64 = "{library}"
{dependencies}''')
        return game

    def test_gdextension_xcframework_slice_and_dependency_are_linked_with_the_entry_symbol(self):
        for rid, identifier in (("iossimulator-arm64", "ios-arm64_x86_64-simulator"), ("ios-arm64", "ios-arm64")):
            with self.subTest(rid=rid), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                game = self.gdextension_project(root)
                pack = root / "exported.pck"
                pack.write_bytes(b"pck")
                properties = f"<TwoDogGodotProjectFullPath>{xml(game)}</TwoDogGodotProjectFullPath>"
                result = msbuild(self.project(root, properties, pack=pack, rid=rid), "Collect", REPO)
                self.assertEqual(0, result.returncode, result.stdout)
                natives = (root / "natives.txt").read_text().splitlines()
                self.assertEqual([f"{game / 'bin/probe.xcframework' / identifier / 'libprobe.a'}|Static|True",
                                  f"{game / 'bin/libdep.a'}|Static|True"], natives)
                symbols = (root / "symbols.txt").read_text().split()
                self.assertEqual(["set_load_from_executable_fn|Function", "probe_init|Function"], symbols)

    def test_gdextension_static_library_links_as_is_and_missing_files_fail_with_codes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            game = self.gdextension_project(root, xcframework=False, dependency=False)
            pack = root / "exported.pck"
            pack.write_bytes(b"pck")
            properties = f"<TwoDogGodotProjectFullPath>{xml(game)}</TwoDogGodotProjectFullPath>"
            result = msbuild(self.project(root, properties, pack=pack), "Collect", REPO)
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertEqual([f"{game / 'bin/libprobe.a'}|Static|True"], (root / "natives.txt").read_text().splitlines())
            # The listed library is gone: TDGI009; opting out skips the extension entirely.
            (game / "bin/libprobe.a").unlink()
            result = msbuild(self.project(root, properties, pack=pack), "Collect", REPO)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("TDGI009", result.stdout)
            result = msbuild(self.project(root, properties + "<TwoDogIosGdExtensions>false</TwoDogIosGdExtensions>", pack=pack), "Collect", REPO)
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertEqual("", (root / "natives.txt").read_text().strip())
        with tempfile.TemporaryDirectory() as directory:
            # An xcframework without a Simulator slice: TDGI010 for the Simulator, fine for devices.
            root = Path(directory)
            game = self.gdextension_project(root, dependency=False)
            plist = game / "bin/probe.xcframework/Info.plist"
            plist.write_text(plist.read_text().replace("<string>simulator</string>", "<string>maccatalyst</string>"))
            pack = root / "exported.pck"
            pack.write_bytes(b"pck")
            properties = f"<TwoDogGodotProjectFullPath>{xml(game)}</TwoDogGodotProjectFullPath>"
            result = msbuild(self.project(root, properties, pack=pack), "Collect", REPO)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("TDGI010", result.stdout)
            result = msbuild(self.project(root, properties, pack=pack, rid="ios-arm64"), "Collect", REPO)
            self.assertEqual(0, result.returncode, result.stdout)

    def test_missing_pack_unsupported_rid_and_native_aot_fail_with_codes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            result = msbuild(self.project(root), "Collect", REPO)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("TDGI006", result.stdout)
            pack = root / "exported.pck"
            pack.write_bytes(b"pck")
            result = msbuild(self.project(root, "<PublishAot>true</PublishAot>", pack), "Collect", REPO)
            self.assertIn("TDGI004", result.stdout)
            result = msbuild(self.project(root, "<RuntimeIdentifier>ios-x64</RuntimeIdentifier>", pack), "Collect", REPO)
            self.assertIn("TDGI005", result.stdout)
            result = msbuild(self.project(root, "<TwoDogVariant>editor</TwoDogVariant>", pack), "Collect", REPO)
            self.assertIn("TDGI001", result.stdout)


if __name__ == "__main__":
    unittest.main()
