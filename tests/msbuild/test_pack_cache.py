"""Check that repacking a package version refreshes its extraction in the package cache.

Run with: python -m unittest discover -s tests/msbuild -v
"""

from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class PackCacheTests(unittest.TestCase):
    def setUp(self):
        scratch_root = ROOT / "temp"
        scratch_root.mkdir(exist_ok=True)
        # Inside the repository, so the root Directory.Build.targets applies as it does to 2dog's packages.
        scratch = tempfile.TemporaryDirectory(prefix="msbuild pack-cache-", dir=scratch_root)
        self.scratch = Path(scratch.name).resolve()
        self.addCleanup(scratch.cleanup)
        self.cache = self.scratch / "cache"
        self.feed = self.scratch / "feed"
        self.package = self.scratch / "package"
        self.package.mkdir()
        (self.package / "probe.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>TwoDog.CacheProbe</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <None Include="payload.txt" Pack="true" PackagePath="payload.txt" />
  </ItemGroup>
</Project>''', encoding="utf-8")
        self.consumer = self.scratch / "consumer"
        self.consumer.mkdir()
        (self.consumer / "consumer.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TwoDog.CacheProbe" Version="1.0.0" />
  </ItemGroup>
</Project>''', encoding="utf-8")

    def dotnet(self, *arguments):
        result = subprocess.run(["dotnet", *arguments, "-nologo"], cwd=self.scratch,
                                capture_output=True, text=True, timeout=300)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def pack(self, payload):
        (self.package / "payload.txt").write_text(payload, encoding="utf-8")
        self.dotnet("pack", str(self.package / "probe.csproj"), "-o", str(self.feed),
                    f"-p:NuGetPackageRoot={self.cache.as_posix()}/")

    def restore(self):
        self.dotnet("restore", str(self.consumer / "consumer.csproj"), "--source", str(self.feed),
                    "--packages", str(self.cache))
        return (self.cache / "twodog.cacheprobe/1.0.0/payload.txt").read_text(encoding="utf-8")

    def test_repacked_version_reaches_the_next_restore(self):
        self.pack("first")
        self.assertEqual(self.restore(), "first")
        self.pack("second")
        self.assertEqual(self.restore(), "second")

    def test_build_time_packs_keep_the_extraction(self):
        # Platform projects pack on every build (GeneratePackageOnBuild) while the same build consumes them.
        self.pack("first")
        self.assertEqual(self.restore(), "first")
        self.dotnet("build", str(self.package / "probe.csproj"), "-p:GeneratePackageOnBuild=true",
                    f"-p:PackageOutputPath={self.feed.as_posix()}/", f"-p:NuGetPackageRoot={self.cache.as_posix()}/")
        self.assertTrue((self.cache / "twodog.cacheprobe/1.0.0/payload.txt").exists())

    def test_other_cached_packages_survive(self):
        other = self.cache / "unrelated/1.0.0/marker.txt"
        other.parent.mkdir(parents=True)
        other.write_text("kept", encoding="utf-8")
        self.pack("first")
        self.assertTrue(other.exists())


if __name__ == "__main__":
    unittest.main()
