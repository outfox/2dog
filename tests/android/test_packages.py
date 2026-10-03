"""Pack real Android NuGets and exercise their targets without the Android workload or device."""
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[2]
JAVA_BUILD_SPEC = importlib.util.spec_from_file_location("build_android_java_package_tests", REPO / "scripts/build_android_java.py")
java_build = importlib.util.module_from_spec(JAVA_BUILD_SPEC)
JAVA_BUILD_SPEC.loader.exec_module(java_build)


def xml(value):
    return str(value).replace("&", "&amp;").replace('"', "&quot;")


class AndroidPackages(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="2dog-android-packages-")
        cls.root = Path(cls.temp.name)
        cls.bin = cls.root / "natives"
        cls.feed = cls.root / "feed"
        # Packaging fixtures only need NETStandard.Library from nuget.org. The repository's
        # local Godot/package feeds do not exist on a clean runner without a native build.
        cls.restore_config = cls.root / "NuGet.Config"
        cls.restore_config.write_text(
            '<configuration><packageSources><clear/>'
            '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>'
            '</packageSources></configuration>')
        cls.restore_packages = os.environ.get("TWODOG_TEST_RESTORE_PACKAGES", str(cls.root / "restore"))
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
            source = cls.root / f"godot-{variant}.aar"
            with zipfile.ZipFile(source, "w") as archive:
                archive.writestr("classes.jar", variant.encode())
                archive.writestr("AndroidManifest.xml", b"manifest")
                archive.writestr("res/values/strings.xml", b"resources")
                for abi in ("arm64-v8a", "x86_64"):
                    archive.writestr(f"jni/{abi}/libgodot_android.so", b"native Godot")
                    archive.writestr(f"jni/{abi}/libc++_shared.so", b"native C++")
            java_build.strip_native_libraries(source, java / variant / "godot.aar")
        for rid in ("android-arm64", "android-x64", "android"):
            command = ["dotnet", "pack", str(REPO / f"platforms/twodog.{rid}/twodog.{rid}.csproj"),
                       "--nologo", f"-p:GodotBinDir={cls.bin.as_posix()}/",
                       f"-p:AndroidJavaPayloadDir={java.as_posix()}/", f"-p:PackageOutputPath={cls.feed}",
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
<TwoDogVariant>{variant}</TwoDogVariant><NuGetPackageRoot>{xml(cache.as_posix())}</NuGetPackageRoot>
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
                with zipfile.ZipFile(io.BytesIO(archive.read(f"android/java/{variant}/godot.aar"))) as aar:
                    self.assertFalse(any(name.startswith("jni/") for name in aar.namelist()), aar.namelist())
                    self.assertEqual(variant.encode(), aar.read("classes.jar"))
                    self.assertEqual(b"manifest", aar.read("AndroidManifest.xml"))
                    self.assertEqual(b"resources", aar.read("res/values/strings.xml"))
            self.assertIn("android/host/TwoDogActivity.java", archive.namelist())
            self.assertIn("buildTransitive/2dog.android.targets", archive.namelist())

    def test_meta_dependencies_pin_both_variants_and_exclude_native_flow(self):
        for rid in ("android-arm64", "android-x64"):
            with zipfile.ZipFile(self.feed / f"2dog.{rid}.{self.version}.nupkg") as archive:
                nuspec = ET.fromstring(archive.read(f"2dog.{rid}.nuspec"))
                deps = nuspec.findall(".//{*}dependency")
                self.assertCountEqual([f"2dog.{rid}.release", f"2dog.{rid}.debug"],
                                      [dep.attrib["id"] for dep in deps])
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

    def test_desktop_native_copy_targets_skip_android_hosts(self):
        host_os = "Windows" if os.name == "nt" else "OSX" if sys.platform == "darwin" else "Linux"
        for platform in ("android", ""):
            with self.subTest(platform=platform):
                directory = Path(tempfile.mkdtemp(dir=self.root))
                (directory / "desktop.so").write_bytes(b"desktop fixture")
                project = directory / "Host.proj"
                project.write_text(f'''<Project>
<PropertyGroup><TargetPlatformIdentifier>{platform}</TargetPlatformIdentifier>
<OutputPath>{xml(directory.as_posix())}/build/</OutputPath><PublishDir>{xml(directory.as_posix())}/publish/</PublishDir>
<NuGetPackageRoot>{xml(directory.as_posix())}/absent/</NuGetPackageRoot></PropertyGroup>
<ItemGroup><TwoDogNativeResolver Include="desktop">
<PackageId>fixture</PackageId><PackageVersion>1</PackageVersion><LibPrefix>libgodot</LibPrefix><Ext>so</Ext>
<HostOs>{host_os}</HostOs><RidPrefix>desktop</RidPrefix><LocalBin>{xml(directory.as_posix())}/</LocalBin>
<LocalRelease>desktop.so</LocalRelease><LocalDebug>desktop.so</LocalDebug><LocalEditor>desktop.so</LocalEditor>
</TwoDogNativeResolver></ItemGroup>
<Import Project="{xml(REPO / 'platforms/common/2dog.native-resolver.targets')}"/>
<Target Name="Build"/><Target Name="Publish"/>
</Project>''')
                result = subprocess.run(["dotnet", "msbuild", str(project), "-t:Build,Publish", "-nologo"],
                                        cwd=REPO, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
                self.assertEqual(0, result.returncode, result.stdout)
                for output in ("build", "publish"):
                    self.assertEqual(platform != "android", (directory / output / "libgodot-release.so").exists())

    def test_editor_unknown_rid_and_missing_payload_fail_loudly(self):
        for arguments, diagnostic in (({"variant": "editor"}, "TDGA001"), ({"rid": "android-x86"}, "TDGA002"),
                                      ({"missing": True}, "TDGA003")):
            result, _ = self.consumer(**arguments)
            self.assertNotEqual(0, result.returncode)
            self.assertIn(diagnostic, result.stdout)

    def test_force_pack_cannot_publish_empty_android_packages(self):
        projects = [REPO / f"platforms/twodog.{rid}/twodog.{rid}{suffix}.csproj"
                    for rid in ("android-arm64", "android-x64") for suffix in ("", ".debug", ".release")]
        projects.append(REPO / "platforms/twodog.android/twodog.android.csproj")
        for project in projects:
            result = subprocess.run(["dotnet", "msbuild", str(project), "-getProperty:IsPackable", "-nologo",
                                     "-p:ForcePackAllPlatforms=true", f"-p:GodotBinDir={self.root.as_posix()}/absent/",
                                     f"-p:AndroidJavaPayloadDir={self.root.as_posix()}/absent/"], cwd=REPO,
                                    text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout)
            self.assertEqual("false", result.stdout.strip())

    def test_incomplete_native_payload_fails_normal_pack_and_skips_forced_pack(self):
        directory = Path(tempfile.mkdtemp(dir=self.root))
        payload = directory / "android/template_release/arm64"
        payload.mkdir(parents=True)
        (payload / "libgodot_android.so").write_bytes(b"native Godot")
        project = REPO / "platforms/twodog.android-arm64/twodog.android-arm64.release.csproj"
        result = subprocess.run(["dotnet", "pack", str(project), "--nologo",
                                 f"-p:GodotBinDir={directory.as_posix()}/", f"-p:PackageOutputPath={directory / 'feed'}",
                                 f"-p:RestoreConfigFile={self.restore_config}", f"-p:RestorePackagesPath={self.restore_packages}",
                                 "-p:NuGetAudit=false"], cwd=REPO, text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180)
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIn("Android payload is missing libc++_shared.so", result.stdout)
        self.assertEqual([], list((directory / "feed").glob("*.nupkg")))
        forced = subprocess.run(["dotnet", "msbuild", str(project), "-getProperty:IsPackable", "-nologo",
                                 "-p:ForcePackAllPlatforms=true", f"-p:GodotBinDir={directory.as_posix()}/"],
                                cwd=REPO, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
        self.assertEqual(0, forced.returncode, forced.stdout)
        self.assertEqual("false", forced.stdout.strip())

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

    GDEXTENSION = '''[configuration]
entry_symbol = "probe_init"

[libraries]
windows.x86_64 = "res://addon/bin/probe.dll"
android.debug.arm64 = "res://addon/bin/libprobe.debug.arm64.so"
android.release.arm64 = "res://addon/bin/libprobe.release.arm64.so"
android.x86_64 = "bin/libprobe.x86_64.so"

[dependencies]
android.arm64 = {
    "res://addon/bin/libdep.arm64.so" : ""
}
'''

    def gdextension_libraries(self, rid, variant, missing=()):
        directory = Path(tempfile.mkdtemp(dir=self.root))
        game = directory / "game"
        for name in ("probe.dll", "libprobe.debug.arm64.so", "libprobe.release.arm64.so", "libprobe.x86_64.so",
                     "libdep.arm64.so"):
            if name not in missing:
                (game / "addon/bin").mkdir(parents=True, exist_ok=True)
                (game / "addon/bin" / name).write_bytes(b"library")
        (game / "addon/probe.gdextension").write_text(self.GDEXTENSION)
        # Godot never loads extensions under .gdignore, so their missing libraries are not an error.
        (game / "host").mkdir()
        (game / "host/.gdignore").touch()
        (game / "host/stale.gdextension").write_text('[libraries]\nandroid.arm64 = "res://absent.so"\n'
                                                     'android.x86_64 = "res://absent.so"\n')
        targets = self.packages / "2dog.android" / self.version / "build/2dog.android.targets"
        project = directory / "Host.proj"
        project.write_text(f'''<Project>
<PropertyGroup><TargetPlatformIdentifier>android</TargetPlatformIdentifier><RuntimeIdentifier>{rid}</RuntimeIdentifier>
<TwoDogVariant>{variant}</TwoDogVariant><TwoDogGodotProjectFullPath>{xml(game)}</TwoDogGodotProjectFullPath></PropertyGroup>
<Import Project="{xml(targets)}"/>
<Target Name="PrepareForBuild"><WriteLinesToFile File="{xml(directory / 'libraries.txt')}"
Lines="@(AndroidNativeLibrary->'%(Abi)|%(Filename)%(Extension)')" Overwrite="true"/></Target>
</Project>''')
        result = subprocess.run(["dotnet", "msbuild", str(project), "-t:PrepareForBuild", "-nologo"], cwd=REPO,
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        listing = directory / "libraries.txt"
        return result, sorted(listing.read_text().splitlines()) if listing.exists() else []

    def test_gdextension_libraries_follow_the_selected_abi_and_variant(self):
        cases = {("android-arm64", "debug"): ["arm64-v8a|libdep.arm64.so", "arm64-v8a|libprobe.debug.arm64.so"],
                 ("android-arm64", "release"): ["arm64-v8a|libdep.arm64.so", "arm64-v8a|libprobe.release.arm64.so"],
                 ("android-x64", "debug"): ["x86_64|libprobe.x86_64.so"]}
        for (rid, variant), expected in cases.items():
            with self.subTest(rid=rid, variant=variant):
                result, libraries = self.gdextension_libraries(rid, variant)
                self.assertEqual(0, result.returncode, result.stdout)
                self.assertEqual(expected, libraries)

    def test_missing_gdextension_library_for_a_selected_abi_fails(self):
        result, _ = self.gdextension_libraries("android-arm64", "debug", missing=("libdep.arm64.so",))
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIn("TDGA009", result.stdout)
        self.assertIn("res://addon/bin/libdep.arm64.so", result.stdout)
        # A library for an unselected ABI may be absent.
        result, _ = self.gdextension_libraries("android-arm64", "debug", missing=("libprobe.x86_64.so",))
        self.assertEqual(0, result.returncode, result.stdout)

    def android_signing(self, variant="release", environment=(), properties=""):
        directory = Path(tempfile.mkdtemp(dir=self.root))
        keystore = directory / "release.keystore"
        keystore.write_bytes(b"keystore")
        targets = self.packages / "2dog.android" / self.version / "build/2dog.android.targets"
        project = directory / "Host.proj"
        project.write_text(f'''<Project>
<PropertyGroup><TargetPlatformIdentifier>android</TargetPlatformIdentifier><TwoDogVariant>{variant}</TwoDogVariant>{properties}</PropertyGroup>
<Import Project="{xml(targets)}"/>
<Target Name="_ResolveAndroidSigningKey"><WriteLinesToFile File="{xml(directory / 'signing.txt')}" Overwrite="true"
Lines="AndroidKeyStore=$(AndroidKeyStore);KeyStore=$(AndroidSigningKeyStore);Alias=$(AndroidSigningKeyAlias);StorePass=$(AndroidSigningStorePass);KeyPass=$(AndroidSigningKeyPass)"/></Target>
</Project>''')
        env = {name: value for name, value in os.environ.items() if not name.startswith("GODOT_ANDROID_KEYSTORE_")}
        env.update({name: value.replace("{keystore}", str(keystore)) for name, value in dict(environment).items()})
        result = subprocess.run(["dotnet", "msbuild", str(project), "-t:_ResolveAndroidSigningKey", "-nologo", "-v:diag"],
                                cwd=REPO, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        listing = directory / "signing.txt"
        signing = dict(line.split("=", 1) for line in listing.read_text().splitlines()) if listing.exists() else {}
        return result, signing, keystore

    RELEASE_KEYSTORE = {"GODOT_ANDROID_KEYSTORE_RELEASE_PATH": "{keystore}", "GODOT_ANDROID_KEYSTORE_RELEASE_USER": "upload",
                        "GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD": "s3cr3t-value"}

    def test_signing_follows_godots_keystore_variables_without_reading_the_password(self):
        result, signing, keystore = self.android_signing(environment=self.RELEASE_KEYSTORE)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual(("true", str(keystore.resolve()), "upload"),
                         (signing["AndroidKeyStore"], signing["KeyStore"], signing["Alias"]))
        self.assertEqual("env:GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD", signing["StorePass"])
        self.assertEqual(signing["StorePass"], signing["KeyPass"])
        # Not even a diagnostic-verbosity log contains the password.
        self.assertNotIn("s3cr3t-value", result.stdout)

    def test_signing_respects_explicit_keystores_and_the_variant(self):
        result, signing, _ = self.android_signing(environment=self.RELEASE_KEYSTORE, properties=(
            "<AndroidKeyStore>true</AndroidKeyStore><AndroidSigningKeyStore>mine.jks</AndroidSigningKeyStore>"))
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual(("mine.jks", ""), (signing["KeyStore"], signing["Alias"]))
        # A debug build reads only the DEBUG variables and otherwise keeps the .NET debug key.
        result, signing, _ = self.android_signing(variant="debug", environment=self.RELEASE_KEYSTORE)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual("", signing["AndroidKeyStore"])
        result, signing, _ = self.android_signing()
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual("", signing["AndroidKeyStore"])
        self.assertIn("signed with the .NET debug key", result.stdout)

    def test_incomplete_signing_configuration_fails(self):
        result, _, _ = self.android_signing(environment={**self.RELEASE_KEYSTORE,
                                                         "GODOT_ANDROID_KEYSTORE_RELEASE_PATH": "absent.jks"})
        self.assertNotEqual(0, result.returncode, result.stdout[-2000:])
        self.assertIn("TDGA011", result.stdout)
        environment = {name: value for name, value in self.RELEASE_KEYSTORE.items() if not name.endswith("PASSWORD")}
        result, _, _ = self.android_signing(environment=environment)
        self.assertNotEqual(0, result.returncode, result.stdout[-2000:])
        self.assertIn("TDGA012", result.stdout)


if __name__ == "__main__":
    unittest.main()
