"""Build identities and release gates must reject stale or incomplete inputs."""
import io
import fnmatch
import json
import os
from pathlib import Path
import tempfile
import shutil
import subprocess
import unittest
from unittest.mock import patch
import zipfile

from scripts import check_published_natives, install_workloads, native_inputs, release_packages

ROOT = Path(__file__).resolve().parents[2]


class NativeIdentityTests(unittest.TestCase):
    def test_native_stamp_is_only_the_fingerprint_with_locked_uv(self):
        with tempfile.TemporaryDirectory(prefix="2dog-native-stamp-") as directory:
            result = subprocess.run(["dotnet", "msbuild",
                str(ROOT / "platforms/twodog.win-x64/twodog.win-x64.release.csproj"),
                "-t:StampNativeInputs", f"-p:IntermediateOutputPath={Path(directory).as_posix()}/", "-nologo"],
                env=os.environ | {"UV_LOCKED": "1"}, capture_output=True, text=True, timeout=60)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertNotIn("warning", result.stdout + result.stderr)
            stamp = (Path(directory) / "native-inputs.txt").read_text().strip()
            self.assertEqual(native_inputs.fingerprint(kind="package"), stamp)

    def identity(self, props, tree=b"recipe", godot=b"a" * 40, kind="build", poe=None):
        def git(command, *args, **kwargs):
            if command == "rev-parse":
                return godot
            if command == "ls-tree":
                self.assertIn("build-godot.py", args)
                self.assertIn("scripts/webgpu.py", args)
                self.assertIn("scripts/build_android_java.py", args)
                self.assertIn("nuget/webgpu", args)
                self.assertIn("global.json", args)
                return tree
            if args[0].endswith(":pyproject.toml"):
                return (poe or '[tool.poe.tasks.build-godot]\ncmd = "python build-godot.py"\n').encode()
            return props.encode()
        with patch.object(native_inputs, "git", side_effect=git):
            return native_inputs.fingerprint(kind=kind)

    def test_build_inputs_change_identity_but_release_counters_do_not(self):
        props = "<Project><EmscriptenVersion>3.1.56</EmscriptenVersion><TwoDogRevision>1</TwoDogRevision></Project>"
        baseline = self.identity(props)
        self.assertEqual(baseline, self.identity(props.replace(">1<", ">2<")))
        self.assertNotEqual(baseline, self.identity(props, tree=b"new compiler flags"))
        self.assertNotEqual(baseline, self.identity(props, godot=b"b" * 40))
        self.assertNotEqual(baseline, self.identity(props.replace("3.1.56", "4.0.1")))
        self.assertEqual(self.identity(props, kind="package"), self.identity(props.replace(">1<", ">2<"), kind="package"))

    def test_published_native_version_requires_matching_input_stamp(self):
        def package(stamp):
            data = io.BytesIO()
            with zipfile.ZipFile(data, "w") as archive:
                if stamp is not None:
                    archive.writestr("2dog-native-inputs.txt", stamp)
            return data.getvalue()
        check_published_natives.verify(package("new"), "new")
        for stamp in (None, "old"):
            with self.subTest(stamp=stamp), self.assertRaises(ValueError):
                check_published_natives.verify(package(stamp), "new")

    def test_managed_poe_edits_do_not_invalidate_native_builds(self):
        props = "<Project><EmscriptenVersion>3.1.56</EmscriptenVersion></Project>"
        poe = '[tool.poe.tasks.build-godot]\ncmd = "python build-godot.py"\n'
        baseline = self.identity(props, poe=poe)
        self.assertEqual(baseline, self.identity(props, poe=poe + '[tool.poe.tasks.build]\ncmd = "pack differently"\n'))
        self.assertNotEqual(baseline, self.identity(props, poe=poe.replace('build-godot.py', 'other-native-builder.py')))
        self.assertNotEqual(baseline, self.identity(props, poe=poe + '[tool.poe.executor]\ntype = "uv"\nlocked = true\n'))

    def test_optional_workloads_use_pin_without_requiring_it_for_desktop_resolution(self):
        config = json.loads((ROOT / "global.json").read_text())
        self.assertNotIn("workloadVersion", config["sdk"])
        with patch.object(install_workloads.subprocess, "run") as run:
            install_workloads.main(["wasm-tools"])
        self.assertIn(config["workloadVersion"], run.call_args.args[0])


class ReleaseGateTests(unittest.TestCase):
    def feed(self, root):
        managed, native = release_packages.expected_packages()
        for name in managed | native:
            version = "4.8.0.200-beta3" if name in managed else "4.8.0.199"
            with zipfile.ZipFile(root / f"{name}.{version}.nupkg", "w") as archive:
                archive.writestr("package.nuspec", f"<package><metadata><id>{name}</id><version>{version}</version></metadata></package>")
        for precision in ("single", "double"):
            (root / f"native-build-{precision}.json").write_text(json.dumps(dict(
                precision=precision, identity="a" * 64, tag="godot-" + "a" * 64 + ("-double" if precision == "double" else ""))))

    def test_release_requires_both_precisions_and_all_integrations(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.feed(root)
            files = release_packages.validate(root, "v4.8.0.200-beta3")
            manifest = dict(tag="v4.8.0.200-beta3", files=files)
            release_packages.validate(root, manifest["tag"], manifest)
            path = root / "2dog.xunit.4.8.0.200-beta3.nupkg"
            original = path.read_bytes()
            path.unlink()
            with self.assertRaisesRegex(ValueError, "Incomplete release"):
                release_packages.validate(root, manifest["tag"])
            path.write_bytes(original + b"changed bytes")
            with self.assertRaisesRegex(ValueError, "differ from"):
                release_packages.validate(root, manifest["tag"], manifest)

    def test_production_gate_requires_the_latest_ios_release_attempt_to_pass(self):
        manifest = dict(run_id="42", run_attempt="2", commit="source")
        source = dict(head_sha="source", path=".github/workflows/ci.yml", run_attempt=3)
        for conclusion in ("success", None, "failure", "skipped"):
            pages = [{"jobs": [{"name": "iOS Release Smoke / Test iOS Simulator", "conclusion": conclusion, "run_attempt": 3},
                                {"name": "iOS Release Smoke / Test iOS Simulator", "conclusion": "success", "run_attempt": 2}]}]
            with self.subTest(conclusion=conclusion), patch.object(release_packages.subprocess, "check_output",
                    side_effect=[json.dumps(source), json.dumps(pages)]) as command:
                if conclusion == "success":
                    release_packages.require_ios_release(manifest, "outfox/2dog")
                else:
                    with self.assertRaisesRegex(ValueError, "promotion is blocked"):
                        release_packages.require_ios_release(manifest, "outfox/2dog")
                self.assertIn("repos/outfox/2dog/actions/runs/42/jobs?filter=all&per_page=100", command.call_args.args[0])

    def test_ios_release_is_not_reused_or_an_upload_dependency(self):
        workflow = (ROOT / ".github/workflows/ci.yml").read_text()
        release = workflow.split("  ios-release:", 1)[1].split("  deploy:", 1)[0]
        self.assertNotIn("tested-bump", release)
        deploy = workflow.split("  deploy:", 1)[1]
        self.assertNotIn("ios-release", deploy.split("    runs-on:", 1)[0])
        self.assertIn("scripts/release_packages.py", deploy)

    def test_ios_download_uses_staged_package_and_sdk_checksums(self):
        for corrupt in (False, True):
            with self.subTest(corrupt=corrupt), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                source, workspace = root / "source", root / "workspace"
                source.mkdir()
                workspace.mkdir()
                self.feed(source)
                sdk = root / "godotsharp-nupkgs.zip"
                with zipfile.ZipFile(sdk, "w") as archive:
                    archive.writestr("Godot.NET.Sdk.4.8.0-beta.3.nupkg", b"pinned fork SDK")
                path = source / "native-build-double.json"
                native = json.loads(path.read_text())
                native["assets"] = {sdk.name: release_packages.sha256(sdk)}
                path.write_text(json.dumps(native))
                manifest = dict(tag="v4.8.0.200-beta3", files=release_packages.validate(source, "v4.8.0.200-beta3"))
                (source / "release-manifest.json").write_text(json.dumps(manifest))

                def download(command, **kwargs):
                    destination = Path(command[command.index("--dir") + 1])
                    destination.mkdir(exist_ok=True)
                    patterns = [command[i + 1] for i, value in enumerate(command) if value == "--pattern"]
                    for path in (*source.iterdir(), sdk):
                        if any(fnmatch.fnmatch(path.name, pattern) for pattern in patterns):
                            shutil.copyfile(path, destination / path.name)
                    if corrupt and "release-manifest.json" in patterns:
                        target = next(destination.glob("2dog.engine.double*.nupkg"))
                        target.write_bytes(target.read_bytes() + b"wrong attempt")

                previous = Path.cwd()
                try:
                    os.chdir(workspace)
                    with patch.object(release_packages.subprocess, "run", side_effect=download):
                        if corrupt:
                            with self.assertRaisesRegex(ValueError, "checksum mismatch"):
                                release_packages.ios_inputs(Path("packages"), manifest["tag"], "outfox/2dog")
                            self.assertFalse(Path("godot").exists())
                        else:
                            release_packages.ios_inputs(Path("packages"), manifest["tag"], "outfox/2dog")
                            self.assertEqual(b"pinned fork SDK", Path("godot/bin/GodotSharp/Tools/nupkgs/Godot.NET.Sdk.4.8.0-beta.3.nupkg").read_bytes())
                finally:
                    os.chdir(previous)


if __name__ == "__main__":
    unittest.main()
