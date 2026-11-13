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

    def archive(self, variant, payload="web"):
        return self.cache / f"2dog.browser-wasm.{variant}/1.0.0/godot-{payload}/libgodot/libgodot.a"

    def project(self, properties=""):
        path = self.scratch / "consumer.csproj"
        web_props = escape((self.package / "buildTransitive/2dog.browser-wasm.props").as_posix())
        web_targets = escape((self.package / "buildTransitive/2dog.browser-wasm.targets").as_posix())
        engine_targets = escape((ROOT / "twodog.engine/build/2dog.engine.targets").as_posix())
        consumer_targets = self.scratch / "consumer.targets"
        consumer_targets.write_text(
            f'<Project><Import Project="{web_targets}"/><Import Project="{engine_targets}"/></Project>',
            encoding="utf-8",
        )
        path.write_text(f'''<Project>
  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk"/>
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
            "-getProperty:TwoDogVariant,TwoDogWebVariant,TwoDogWebNativeDir,WasmEmitSymbolMap",
            *arguments,
        )
        return json.loads(output)["Properties"]

    def test_web_pack_export_matches_selected_native_variant(self):
        project = self.project()
        paths = set()
        for configuration, variant in (("Debug", "debug"), ("Release", "release"),
                                       ("Release", "debug"), ("Debug", "release")):
            with self.subTest(configuration=configuration, variant=variant):
                output = self.run_msbuild(
                    project, f"-p:Configuration={configuration}", f"-p:TwoDogWebVariant={variant}",
                    "-getProperty:_TwoDogWebPackPath,_TwoDogWebExportDebugArg,_TwoDogWebEditorDebugArg",
                )
                values = json.loads(output)["Properties"]
                paths.add(values["_TwoDogWebPackPath"])
                self.assertEqual(values["_TwoDogWebExportDebugArg"], "--debug" if variant == "debug" else "")
                self.assertEqual(values["_TwoDogWebEditorDebugArg"], '--export-debug "Web"' if variant == "debug" else "")
        self.assertEqual(len(paths), 4, "each configuration/native variant needs an independent pack")

    def test_content_pipeline_ignores_ambient_editor_variables(self):
        editor = escape((self.scratch / "unrelated-editor.exe").as_posix())
        for property_name in ("GODOT_EDITOR", "GODOT4", "GodotEditor", "TwoDogExternalGodotEditor"):
            with self.subTest(property=property_name):
                project = self.project(f"<{property_name}>{editor}</{property_name}>")
                result = self.run_msbuild(project, "-t:TwoDogResolveContentCapability", "-getProperty:TwoDogExternalGodotEditor")
                self.assertEqual(result.strip(), "" if property_name in ("GODOT_EDITOR", "GODOT4") else editor)

    def test_missing_debug_payload_does_not_use_release(self):
        self.archive("debug").unlink()
        project = self.project()
        values = self.evaluate(project, "Debug")
        self.assertEqual(values["TwoDogWebVariant"], "debug")
        self.assertEqual(values["TwoDogWebNativeDir"], "")
        output = self.run_msbuild(project, "-p:Configuration=Debug", "-t:TwoDogWebCheckNativeDir", success=False)
        self.assertIn("debug web payload (libgodot.a) not found", output)

    def test_threaded_hosts_select_the_threaded_payload_only(self):
        threaded = self.archive("release", "web-threads")
        threaded.parent.mkdir(parents=True)
        threaded.write_bytes(b"fixture archive; no native linking required")
        project = self.project("<WasmEnableThreads>true</WasmEnableThreads>")
        output = json.loads(self.run_msbuild(
            project, "-p:Configuration=Release", "-getProperty:TwoDogWebNativeDir",
            "-getItem:TwoDogGodotEmccExtraLDFlag",
        ))
        selected = Path(output["Properties"]["TwoDogWebNativeDir"]) / "libgodot/libgodot.a"
        self.assertEqual(selected.resolve(), threaded.resolve())
        flags = self.link_flags(output)
        self.assertIn("-sDEFAULT_PTHREAD_STACK_SIZE=5120KB", flags)
        # Side modules load on every thread: blocked threads catch up after futex waits, in reserved table slots.
        self.assertIn("-Wl,--wrap=emscripten_futex_wait", flags)
        self.assertIn("-sTABLE_BASE=16385", flags)

        # A single-threaded archive cannot link into the threaded runtime, so it is never substituted.
        values = self.evaluate(project, "Debug")
        self.assertEqual(values["TwoDogWebNativeDir"], "")
        output = self.run_msbuild(project, "-p:Configuration=Debug", "-t:TwoDogWebCheckNativeDir", success=False)
        self.assertIn("godot/bin/web-threads/template_debug/", output)

        output = json.loads(self.run_msbuild(
            self.project(), "-p:Configuration=Release", "-getItem:TwoDogGodotEmccExtraLDFlag",
        ))
        self.assertFalse([flag for flag in self.link_flags(output) if "PTHREAD" in flag or "futex" in flag or "TABLE_BASE" in flag])

    @staticmethod
    def link_flags(output):
        return [item["Identity"] for item in output["Items"]["TwoDogGodotEmccExtraLDFlag"]]

    def test_webgpu_payload_supplies_port_flags_and_a_writable_compiler_cache(self):
        port = self.archive("debug").parent / "emdawnwebgpu/emdawnwebgpu.port.py"
        port.parent.mkdir(parents=True)
        port.touch()
        cache = self.scratch / "writable emscripten cache"
        project = self.project()
        output = json.loads(self.run_msbuild(
            project, "-p:Configuration=Debug", "-t:TwoDogWebGpuEnvironment",
            f"-p:TwoDogWebGpuCachePath={cache}",
            "-getItem:TwoDogGodotEmccExtraLDFlag,EmscriptenEnvVars",
        ))
        flags = self.link_flags(output)
        self.assertIn(f'--use-port="{port.as_posix()}"', flags)
        self.assertTrue(any(flag.startswith('--closure-args="--externs=\'') for flag in flags))
        environment = [item["Identity"] for item in output["Items"]["EmscriptenEnvVars"]]
        self.assertIn(f"EM_CACHE={cache}", environment)
        self.assertIn("EM_FROZEN_CACHE=0", environment)
        self.assertTrue(cache.is_dir())

    def test_web_editor_and_invalid_variant_fail_clearly(self):
        project = self.project()
        output = self.run_msbuild(project, "-p:Configuration=Editor", "-t:TwoDogWebCheckNativeDir", success=False)
        self.assertIn("editor native variant is not available for Web hosts", output)
        output = self.run_msbuild(project, "-p:TwoDogWebVariant=fast", "-t:TwoDogWebCheckNativeDir", success=False)
        self.assertIn("invalid TwoDogWebVariant 'fast'", output)


if __name__ == "__main__":
    unittest.main()
