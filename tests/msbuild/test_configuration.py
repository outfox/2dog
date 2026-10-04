"""Check configuration defaults against the installed SDK without linking Godot.

Requires .NET 10 and wasm-tools. Run with: python -m unittest discover -s tests/msbuild -v
"""

from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape
import json


ROOT = Path(__file__).resolve().parents[2]
WEB = ROOT / "platforms/twodog.browser-wasm"


class ConfigurationTests(unittest.TestCase):
    def setUp(self):
        scratch_root = ROOT / "temp"
        scratch_root.mkdir(exist_ok=True)
        scratch = tempfile.TemporaryDirectory(prefix="msbuild config-", dir=scratch_root)
        self.scratch = Path(scratch.name).resolve()
        assert self.scratch.is_relative_to(scratch_root.resolve())
        self.addCleanup(scratch.cleanup)
        self.cache = self.scratch / "packages"
        self.package = self.cache / "2dog.browser-wasm/1.0.0"
        for folder in ("build", "buildTransitive"):
            destination = self.package / folder
            destination.mkdir(parents=True)
            for source in (WEB / folder).iterdir():
                if source.is_file():
                    shutil.copyfile(source, destination / source.name)
        for variant in ("debug", "release"):
            archive = self.archive(variant)
            archive.parent.mkdir(parents=True)
            archive.write_bytes(b"fixture archive; no native linking required")

    def archive(self, variant):
        return self.cache / f"2dog.browser-wasm.{variant}/1.0.0/godot-web/libgodot/libgodot.a"

    def project(self, properties="", early_hook=False):
        path = self.scratch / "consumer.csproj"
        web_props = escape((self.package / "buildTransitive/2dog.browser-wasm.props").as_posix())
        web_targets = escape((self.package / "buildTransitive/2dog.browser-wasm.targets").as_posix())
        engine_targets = escape((ROOT / "twodog.engine/build/2dog.engine.targets").as_posix())
        consumer_targets = self.scratch / "consumer.targets"
        consumer_targets.write_text(
            f'<Project><Import Project="{web_targets}"/><Import Project="{engine_targets}"/></Project>',
            encoding="utf-8",
        )
        extra_hook = ""
        if early_hook:
            hook = self.scratch / "existing-hook.props"
            hook.write_text('<Project><PropertyGroup><ExistingHookRan>true</ExistingHookRan></PropertyGroup></Project>', encoding="utf-8")
            extra_hook = f'<PropertyGroup><BeforeMicrosoftNETSdkTargets>{escape(hook.as_posix())}</BeforeMicrosoftNETSdkTargets></PropertyGroup>'
        path.write_text(f'''<Project>
  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk"/>
  {extra_hook}
  <Import Project="{web_props}"/>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <NuGetPackageRoot>{escape(self.cache.as_posix())}/</NuGetPackageRoot>
    <CustomAfterMicrosoftCommonTargets>{escape(consumer_targets.as_posix())}</CustomAfterMicrosoftCommonTargets>
    {properties}
  </PropertyGroup>
  <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk"/>
</Project>''', encoding="utf-8")
        return path

    def run_msbuild(self, project, *arguments, success=True):
        result = subprocess.run(
            ["dotnet", "msbuild", str(project), "-nologo", *arguments],
            cwd=ROOT, capture_output=True, text=True, timeout=60,
        )
        if success:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout + result.stderr

    def evaluate(self, project, configuration, *arguments):
        output = self.run_msbuild(
            project, f"-p:Configuration={configuration}",
            "-getProperty:TwoDogVariant,TwoDogWebVariant,TwoDogWebNativeDir,WasmEmitSymbolMap,ExistingHookRan",
            *arguments,
        )
        return json.loads(output)["Properties"]

    def test_defaults_follow_configuration_with_real_sdk_imports(self):
        project = self.project()
        for configuration, variant, symbols in (
            ("Debug", "debug", "true"),
            ("Release", "release", "false"),
            ("Editor", "editor", "true"),
            ("Staging", "release", "true"),
        ):
            with self.subTest(configuration=configuration):
                values = self.evaluate(project, configuration)
                self.assertEqual(values["TwoDogVariant"], variant)
                self.assertEqual(values["TwoDogWebVariant"], variant)
                self.assertEqual(values["WasmEmitSymbolMap"], symbols)
                if variant != "editor":
                    selected = Path(values["TwoDogWebNativeDir"]) / "libgodot/libgodot.a"
                    self.assertEqual(selected.resolve(), self.archive(variant).resolve())

    def test_command_line_overrides_win(self):
        project = self.project()
        for configuration, variant, symbols in (
            ("Release", "debug", "true"), ("Debug", "release", "false"),
        ):
            with self.subTest(configuration=configuration):
                values = self.evaluate(
                    project, configuration, f"-p:TwoDogVariant={variant}",
                    f"-p:TwoDogWebVariant={variant}", f"-p:WasmEmitSymbolMap={symbols}",
                )
                self.assertEqual(values["TwoDogVariant"], variant)
                self.assertEqual(values["TwoDogWebVariant"], variant)
                self.assertEqual(values["WasmEmitSymbolMap"], symbols)

    def test_project_overrides_and_configuration_are_read_after_props(self):
        for configuration, variant, symbols in (
            ("Release", "debug", "true"), ("Debug", "release", "false"),
        ):
            with self.subTest(configuration=configuration):
                project = self.project(
                    f"<Configuration>{configuration}</Configuration>"
                    f"<TwoDogVariant>{variant}</TwoDogVariant>"
                    f"<TwoDogWebVariant>{variant}</TwoDogWebVariant>"
                    f"<WasmEmitSymbolMap>{symbols}</WasmEmitSymbolMap>"
                )
                values = json.loads(self.run_msbuild(
                    project, "-getProperty:TwoDogVariant,TwoDogWebVariant,WasmEmitSymbolMap",
                ))["Properties"]
                self.assertEqual(values["TwoDogVariant"], variant)
                self.assertEqual(values["TwoDogWebVariant"], variant)
                self.assertEqual(values["WasmEmitSymbolMap"], symbols)

    def test_existing_sdk_hook_is_preserved(self):
        values = self.evaluate(self.project(early_hook=True), "Release")
        self.assertEqual(values["ExistingHookRan"], "true")
        self.assertEqual(values["WasmEmitSymbolMap"], "false")

    def test_configuration_set_in_project_body_controls_defaults(self):
        project = self.project("<Configuration>Release</Configuration>")
        values = json.loads(self.run_msbuild(
            project, "-getProperty:TwoDogVariant,TwoDogWebVariant,WasmEmitSymbolMap",
        ))["Properties"]
        self.assertEqual(values["TwoDogVariant"], "release")
        self.assertEqual(values["TwoDogWebVariant"], "release")
        self.assertEqual(values["WasmEmitSymbolMap"], "false")

    def test_android_import_order_does_not_change_default_variant(self):
        engine = escape((ROOT / "twodog.engine/build/2dog.engine.targets").as_posix())
        android = escape((ROOT / "platforms/twodog.android/build/2dog.android.targets").as_posix())
        path = self.scratch / "android.proj"
        for imports in ((engine, android), (android, engine)):
            path.write_text(
                '<Project><PropertyGroup><TargetPlatformIdentifier>android</TargetPlatformIdentifier></PropertyGroup>'
                + ''.join(f'<Import Project="{source}"/>' for source in imports) + '</Project>', encoding="utf-8",
            )
            for configuration, expected in (("Debug", "debug"), ("Release", "release"), ("Editor", "editor")):
                with self.subTest(configuration=configuration, imports=imports):
                    output = self.run_msbuild(path, f"-p:Configuration={configuration}", "-getProperty:TwoDogVariant")
                    self.assertEqual(output.strip(), expected)

    def test_standalone_desktop_resolver_follows_configuration(self):
        resolver = escape((ROOT / "platforms/common/2dog.native-resolver.targets").as_posix())
        path = self.scratch / "desktop.proj"
        path.write_text(f'<Project><Import Project="{resolver}"/></Project>', encoding="utf-8")
        for configuration, expected in (("Debug", "debug"), ("Release", "release"), ("Editor", "editor")):
            with self.subTest(configuration=configuration):
                output = self.run_msbuild(
                    path, f"-p:Configuration={configuration}", "-t:TwoDogResolveNativeLibs", "-getProperty:_TwoDogNativeVariant",
                )
                self.assertEqual(output.strip(), expected)

    def test_missing_debug_payload_does_not_use_release(self):
        self.archive("debug").unlink()
        project = self.project()
        values = self.evaluate(project, "Debug")
        self.assertEqual(values["TwoDogWebVariant"], "debug")
        self.assertEqual(values["TwoDogWebNativeDir"], "")
        output = self.run_msbuild(project, "-p:Configuration=Debug", "-t:TwoDogWebCheckNativeDir", success=False)
        self.assertIn("debug web payload (libgodot.a) not found", output)

    def test_web_editor_and_invalid_variant_fail_clearly(self):
        project = self.project()
        output = self.run_msbuild(project, "-p:Configuration=Editor", "-t:TwoDogWebCheckNativeDir", success=False)
        self.assertIn("editor native variant is not available for Web hosts", output)
        output = self.run_msbuild(project, "-p:TwoDogWebVariant=fast", "-t:TwoDogWebCheckNativeDir", success=False)
        self.assertIn("invalid TwoDogWebVariant 'fast'", output)


if __name__ == "__main__":
    unittest.main()
