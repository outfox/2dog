"""Precision build and NuGet integration tests; no engine or platform workload required.

Run with: uv run python -m unittest discover -s tests/precision -v
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from xml.sax.saxutils import escape
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("precision_build", ROOT / "build-godot.py")
build = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = build
spec.loader.exec_module(build)


class NativePrecisionTests(unittest.TestCase):
    def test_editor_and_glue_use_the_same_precision(self):
        config = build.get_platform_config("windows", "x86_64", "double")
        self.assertIn("editor.double.x86_64", config.godot_exe)
        args = argparse.Namespace(precision="double", dev_build="no", scu_build="yes", cache_path="")
        with patch.object(build, "run_with_live_output") as run:
            build.build_editor(args, config)
        self.assertIn("precision=double", run.call_args.args[0])
        with tempfile.TemporaryDirectory() as scratch, patch.object(build, "run_with_live_output") as run:
            old = Path.cwd()
            try:
                os.chdir(scratch)
                api = Path("godot/bin/GodotSharp/Api/Debug/GodotSharp.dll")
                api.parent.mkdir(parents=True)
                api.write_bytes(b"double bindings")
                build.generate_glue(config, "double")
                self.assertEqual(b"double bindings", Path("godot/bin/double/GodotSharp/Api/Debug/GodotSharp.dll").read_bytes())
            finally:
                os.chdir(old)
        commands = [call.args[0] for call in run.call_args_list]
        self.assertEqual(config.godot_exe, commands[0][0])
        self.assertEqual(["uv", "run"], commands[1][:2])
        self.assertEqual("double", commands[1][commands[1].index("--precision") + 1])
        self.assertFalse(any("--clear" in command for command in commands))

    def test_desktop_stages_only_the_double_native(self):
        with tempfile.TemporaryDirectory() as scratch:
            old = Path.cwd()
            try:
                os.chdir(scratch)
                output = Path("godot/bin")
                output.mkdir(parents=True)
                name = "godot.windows.template_debug.double.x86_64.shared_library.dll"
                (output / name).write_bytes(b"double native")
                (output / name.replace(".double", "")).write_bytes(b"single native")
                args = argparse.Namespace(precision="double", target="template_debug", dev_build="no",
                                          scu_build="yes", debug_symbols="no", cache_path="")
                with patch.object(build, "run_with_live_output") as run:
                    build.build_libgodot(args, build.get_platform_config("windows", "x86_64", "double"))
                self.assertIn("precision=double", run.call_args.args[0])
                self.assertEqual(b"double native", (output / "double" / name).read_bytes())
                self.assertFalse((output / "double" / name.replace(".double", "")).exists())
            finally:
                os.chdir(old)


class PackagePrecisionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.scratch = tempfile.TemporaryDirectory(prefix="2dog-precision-")
        cls.root = Path(cls.scratch.name)
        shutil.copyfile(ROOT / "global.json", cls.root / "global.json")
        (cls.root / "Directory.Build.props").write_text("<Project/>")
        (cls.root / "Directory.Build.targets").write_text("<Project/>")
        cls.feed = cls.root / "feed"
        cls.feed.mkdir()
        (cls.root / "NuGet.Config").write_text(
            '<configuration><config><add key="globalPackagesFolder" value="cache"/></config>'
            f'<packageSources><clear/><add key="fixture" value="{escape(str(cls.feed))}"/>'
            '</packageSources></configuration>')
        for precision in ("single", "double"):
            package = "2dog.precision-test" + (".double" if precision == "double" else "")
            directory = cls.root / precision
            directory.mkdir()
            (directory / "buildTransitive").mkdir()
            (directory / "buildTransitive/2dog.precision-test.targets").write_text(
                '<Project><PropertyGroup><PrecisionTargetsImported>true</PrecisionTargetsImported></PropertyGroup></Project>')
            (directory / "fixture.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                f'<PackageId>{package}</PackageId><Version>1.0.0</Version><TwoDogPrecision>{precision}</TwoDogPrecision>'
                f'<PackageOutputPath>{escape(str(cls.feed))}</PackageOutputPath>'
                '<IncludeBuildOutput>false</IncludeBuildOutput><SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>'
                '</PropertyGroup>'
                f'<Import Project="{escape(str(ROOT / "build/2dog.precision-pack.targets"))}"/></Project>')
            cls.run_dotnet("pack", directory / "fixture.csproj", cwd=cls.root)

        # Legacy meta packages carry no precision stamp. The wrapper exercises transitive resolution.
        for package, dependencies in (("2dog.engine", ""),
                ("2dog.avalonia", ""),
                ("2dog.blazor", ""),
                ("2dog.xunit", ""),
                ("2dog.nunit", ""),
                ("2dog.repl", ""),
                ("2dog.precision-neutral-wrapper", '<dependencies><dependency id="2dog.engine" version="[1.0.0]"/></dependencies>')):
            with zipfile.ZipFile(cls.feed / f"{package}.1.0.0.nupkg", "w") as archive:
                archive.writestr(package + ".nuspec", '<package><metadata>'
                    f'<id>{package}</id><version>1.0.0</version><authors>Test</authors>'
                    f'<description>Legacy fixture</description>{dependencies}</metadata></package>')
                archive.writestr("lib/net10.0/_._", "")

    @classmethod
    def tearDownClass(cls):
        cls.scratch.cleanup()

    @classmethod
    def run_dotnet(cls, *command, cwd, error=None):
        result = subprocess.run(["dotnet", *map(str, command), "--nologo"], cwd=cwd,
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        if error:
            if not result.returncode or error not in result.stdout:
                raise AssertionError(result.stdout)
        elif result.returncode or "warning " in result.stdout:
            raise AssertionError(result.stdout)
        return result.stdout

    def project(self, name, packages, properties=""):
        directory = self.root / name
        directory.mkdir()
        refs = "".join(f'<PackageReference Include="{package}" Version="1.0.0"/>' for package in packages)
        project = directory / "consumer.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                           f'{properties}</PropertyGroup><ItemGroup>{refs}</ItemGroup></Project>')
        return project

    def test_double_assets_auto_import_on_first_restore(self):
        project = self.project("double-consumer", ["2dog.precision-test.double"])
        self.run_dotnet("build", project, cwd=self.root)
        values = json.loads(self.run_dotnet("msbuild", project,
            "-getProperty:TwoDogPrecision,GodotFloat64,PrecisionTargetsImported,DefineConstants", cwd=self.root))["Properties"]
        self.assertEqual("double", values["TwoDogPrecision"])
        self.assertEqual("true", values["GodotFloat64"])
        self.assertEqual("true", values["PrecisionTargetsImported"])
        self.assertIn("REAL_T_IS_DOUBLE", values["DefineConstants"])
        with zipfile.ZipFile(self.feed / "2dog.precision-test.double.1.0.0.nupkg") as package:
            self.assertIn("buildTransitive/2dog.precision-test.double.targets", package.namelist())
            self.assertNotIn("buildTransitive/2dog.precision-test.targets", package.namelist())

    def test_single_assets_keep_their_existing_identity(self):
        project = self.project("single-consumer", ["2dog.precision-test"])
        self.run_dotnet("build", project, cwd=self.root)
        value = self.run_dotnet("msbuild", project, "-getProperty:TwoDogPrecision", cwd=self.root)
        self.assertEqual("single", value.strip())

    def test_mixed_precision_packages_fail_before_compilation(self):
        for precision in ("single", "double"):
            with self.subTest(precision=precision):
                project = self.project("mixed-" + precision,
                    ["2dog.precision-test", "2dog.precision-test.double"],
                    f"<TwoDogPrecision>{precision}</TwoDogPrecision>")
                self.run_dotnet("build", project, cwd=self.root, error="TDG005")

    def test_legacy_single_packages_are_rejected_in_direct_and_transitive_dependencies(self):
        for package in ("2dog.engine", "2dog.precision-neutral-wrapper"):
            with self.subTest(package=package):
                project = self.project("legacy-" + package, ["2dog.precision-test.double", package])
                self.run_dotnet("build", project, cwd=self.root, error="TDG005")

    def test_explicit_precision_must_match_the_package(self):
        project = self.project("wrong-precision", ["2dog.precision-test.double"],
                               "<TwoDogPrecision>single</TwoDogPrecision>")
        self.run_dotnet("build", project, cwd=self.root, error="TDG005")

    def test_precision_neutral_integrations_are_allowed_with_double_packages(self):
        project = self.project("neutral-integrations", ["2dog.precision-test.double",
            "2dog.avalonia", "2dog.blazor", "2dog.xunit", "2dog.nunit", "2dog.repl"])
        self.run_dotnet("build", project, cwd=self.root)


class NativeResolverPrecisionTests(unittest.TestCase):
    def test_double_resolvers_select_double_packages_and_local_files(self):
        platforms = (("win-x64", "windows", "x86_64", "godot", "dll", "TwoDogWinX64PackageVersion"),
                     ("linux-x64", "linuxbsd", "x86_64", "libgodot", "so", "TwoDogLinuxX64PackageVersion"),
                     ("osx-arm64", "macos", "arm64", "libgodot", "dylib", "TwoDogOsxArm64PackageVersion"))
        with tempfile.TemporaryDirectory(prefix="2dog-resolve-double-") as scratch:
            directory = Path(scratch)
            cache, local = directory / "cache", directory / "local"
            local.mkdir()
            for rid, platform, arch, prefix, extension, version_property in platforms:
                for variant, target in (("debug", "template_debug"), ("release", "template_release"), ("editor", "editor")):
                    with self.subTest(rid=rid, variant=variant):
                        targets = ROOT / f"platforms/twodog.{rid}/build/2dog.{rid}.targets"
                        project = directory / "resolve.proj"
                        project.write_text(f'<Project><PropertyGroup><TwoDogPackageSuffix>.double</TwoDogPackageSuffix>'
                            '<TwoDogNativePrecisionSuffix>.double</TwoDogNativePrecisionSuffix>'
                            f'<TwoDogVariant>{variant}</TwoDogVariant><{version_property}>1.0.0</{version_property}>'
                            f'<NuGetPackageRoot>{escape(str(cache))}/</NuGetPackageRoot>'
                            f'<TwoDogLocalGodotBin>{escape(str(local))}/</TwoDogLocalGodotBin>'
                            f'</PropertyGroup><Import Project="{escape(str(targets))}"/></Project>')
                        payload = cache / f"2dog.{rid}.{variant}.double/1.0.0/runtimes/{rid}/native/libgodot-{variant}.{extension}"
                        single = cache / f"2dog.{rid}.{variant}/1.0.0/runtimes/{rid}/native/libgodot-{variant}.{extension}"
                        for file in (payload, single):
                            file.parent.mkdir(parents=True, exist_ok=True)
                            file.write_bytes(b"fixture")
                        def resolve():
                            result = subprocess.run(["dotnet", "msbuild", str(project), "-nologo",
                                "-t:TwoDogResolveNativeLibs", "-getItem:_TwoDogNativeResolved"],
                                capture_output=True, text=True, timeout=60)
                            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                            return json.loads(result.stdout)["Items"]["_TwoDogNativeResolved"][0]
                        self.assertEqual(payload.resolve(), Path(resolve()["Resolved"]).resolve())
                        payload.unlink()
                        # A restored single-precision variant must never satisfy a double request.
                        self.assertEqual("", resolve()["Resolved"])
                        fallback = local / f"{prefix}.{platform}.{target}.double.{arch}.shared_library.{extension}"
                        fallback.write_bytes(b"double local")
                        self.assertEqual(fallback.resolve(), Path(resolve()["Resolved"]).resolve())
                        fallback.unlink()


class BuildWorkerLimitTests(unittest.TestCase):
    def test_scons_respects_dotnet_processor_count(self):
        for value, cap, expected in ((None, None, None), (None, 4, 4),
                                     ("3", None, 3), ("3", 8, 3), ("8", 3, 3)):
            with self.subTest(value=value, cap=cap), patch.dict(os.environ, {}, clear=True), \
                    patch.object(build, "get_job_limit", return_value=cap), \
                    patch.object(os, "cpu_count", return_value=16):
                if value is not None:
                    os.environ["DOTNET_PROCESSOR_COUNT"] = value
                self.assertEqual(expected, build.get_scons_job_limit())

    def test_scons_rejects_invalid_dotnet_processor_count(self):
        for value in ("0", "-1", "invalid", ""):
            with self.subTest(value=value), patch.dict(os.environ, DOTNET_PROCESSOR_COUNT=value):
                with self.assertRaises(ValueError):
                    build.get_scons_job_limit()

    def test_machine_worker_cap_is_enforced_by_msbuild(self):
        with tempfile.TemporaryDirectory(prefix="2dog-build-cap-") as scratch:
            project = Path(scratch) / "cap.proj"
            project.write_text('<Project><Target Name="Check"><Message Importance="high" '
                               'Text="NODES=$(MSBuildNodeCount);CPUS=$([System.Environment]::ProcessorCount)"/></Target></Project>')
            environment = os.environ.copy()
            environment["TWODOG_MAX_BUILD_JOBS"] = "20"
            for explicit in ([], ["-m:2"]):
                expected = min(20, os.cpu_count() or 1, 2 if explicit else 20)
                result = subprocess.run(["uv", "run", "--project", str(ROOT), "python",
                    str(ROOT / "scripts/dotnet_build.py"), "msbuild", str(project), "-t:Check", "-nologo", *explicit],
                    env=environment, capture_output=True, text=True, timeout=60)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertIn(f"NODES={expected};CPUS={expected}", result.stdout)

    def test_native_command_uses_the_same_worker_cap(self):
        args = argparse.Namespace(precision="double", dev_build="no", scu_build="yes", cache_path="")
        with patch.object(build, "get_scons_job_limit", return_value=20), patch.object(build, "run_with_live_output") as run:
            build.build_editor(args, build.get_platform_config("windows", "x86_64", "double"))
        self.assertIn("--jobs=20", run.call_args.args[0])


class PrecisionOutputIsolationTests(unittest.TestCase):
    def test_switching_precision_does_not_compile_other_generated_outputs(self):
        with tempfile.TemporaryDirectory(prefix="2dog-output-isolation-") as scratch:
            root = Path(scratch)
            shutil.copyfile(ROOT / "global.json", root / "global.json")
            (root / "Directory.Build.props").write_text(
                f'<Project><Import Project="{escape(str(ROOT / "Directory.Build.props"))}"/></Project>')
            (root / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
            (root / "Code.cs").write_text('public class Probe {}')
            for precision in ("single", "double", "single"):
                result = subprocess.run(["dotnet", "build", str(root / "Probe.csproj"),
                    f"-p:TwoDogPrecision={precision}", "-nologo", "-m:2"],
                    capture_output=True, text=True, timeout=120)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertTrue((root / "bin/Debug/net10.0/Probe.dll").is_file())
            self.assertTrue((root / "bin/double/Debug/net10.0/Probe.dll").is_file())


if __name__ == "__main__":
    unittest.main()
