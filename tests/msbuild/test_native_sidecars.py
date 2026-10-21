"""Verify native dependencies travel with the selected variant for build and publish."""

import platform
import subprocess
import tempfile
import unittest
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class NativeSidecarTests(unittest.TestCase):
    def test_package_and_local_dependencies_follow_the_selected_variant(self):
        for use_package in (True, False):
            for variant in ("debug", "release", "editor"):
                with (
                    self.subTest(package=use_package, variant=variant),
                    tempfile.TemporaryDirectory(dir=ROOT / "temp") as directory,
                ):
                    scratch = Path(directory)
                    cache = scratch / "packages"
                    local = scratch / "local"
                    native = cache / f"2dog.win-x64.{variant}/1.0.0/runtimes/win-x64/native"
                    sidecars = native if use_package else local / "webgpu/win-x64"
                    sidecars.mkdir(parents=True)
                    (sidecars / "licenses").mkdir()
                    (sidecars / "godot_webgpu_dawn.dll").write_bytes(b"Dawn")
                    (sidecars / "dxcompiler.dll").write_bytes(b"DXC")
                    (sidecars / "dxil.dll").write_bytes(b"DXIL")
                    (sidecars / "licenses/Dawn.txt").write_bytes(b"license")
                    local.mkdir(exist_ok=True)
                    (local / f"godot-{variant}.dll").write_bytes(b"local Godot")
                    if use_package:
                        (native / f"libgodot-{variant}.dll").write_bytes(b"packaged Godot")
                    project = scratch / "consumer.proj"
                    project.write_text(f'''<Project>
<PropertyGroup><TwoDogVariant>{variant}</TwoDogVariant><NuGetPackageRoot>{escape(cache.as_posix())}</NuGetPackageRoot>
<OutputPath>{escape((scratch / "build").as_posix())}/</OutputPath><PublishDir>{escape((scratch / "publish").as_posix())}/</PublishDir>
<RuntimeIdentifier>win-x64</RuntimeIdentifier></PropertyGroup>
<ItemGroup><TwoDogNativeResolver Include="win-x64"><PackageId>2dog.win-x64</PackageId><PackageVersion>1.0.0</PackageVersion>
<LibPrefix>libgodot</LibPrefix><Ext>dll</Ext><HostOs>{platform.system()}</HostOs><RidPrefix>win</RidPrefix>
<LocalBin>{escape(local.as_posix())}/</LocalBin><LocalRelease>godot-release.dll</LocalRelease>
<LocalDebug>godot-debug.dll</LocalDebug><LocalEditor>godot-editor.dll</LocalEditor></TwoDogNativeResolver></ItemGroup>
<Import Project="{escape((ROOT / "platforms/common/2dog.native-resolver.targets").as_posix())}"/>
</Project>''')
                    result = subprocess.run(
                        [
                            "dotnet",
                            "msbuild",
                            str(project),
                            "-nologo",
                            "-t:TwoDogCopyNativeLibs;TwoDogPublishNativeLibs",
                        ],
                        cwd=ROOT,
                        capture_output=True,
                        text=True,
                    )
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                    for folder in ("build", "publish"):
                        output = scratch / folder
                        self.assertEqual(
                            (output / f"libgodot-{variant}.dll").read_bytes(),
                            b"packaged Godot" if use_package else b"local Godot",
                        )
                        self.assertEqual((output / "godot_webgpu_dawn.dll").read_bytes(), b"Dawn")
                        self.assertEqual((output / "dxcompiler.dll").read_bytes(), b"DXC")
                        self.assertEqual((output / "dxil.dll").read_bytes(), b"DXIL")
                        self.assertEqual((output / "licenses/Dawn.txt").read_bytes(), b"license")
                        self.assertFalse((output / f"godot-{variant}.dll").exists())


if __name__ == "__main__":
    unittest.main()
