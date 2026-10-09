"""Exercise the consumer build target without a restore or native engine."""

from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]


class ReplPublishSettingsTests(unittest.TestCase):
    def setUp(self):
        scratch = tempfile.TemporaryDirectory()
        self.addCleanup(scratch.cleanup)
        self.project = Path(scratch.name) / "consumer.proj"
        targets = escape((ROOT / "twodog.repl/buildTransitive/2dog.repl.targets").as_posix())
        self.project.write_text(f'<Project><Import Project="{targets}"/><Target Name="PrepareForBuild"/><Target Name="PrepareForPublish"/></Project>')

    def run_target(self, target, *properties):
        return subprocess.run(["dotnet", "msbuild", str(self.project), "-nologo", f"-t:{target}", *properties],
                              cwd=ROOT, capture_output=True, text=True, timeout=30)

    def test_jit_build_and_publish_are_allowed_in_all_native_variants(self):
        for configuration in ("Debug", "Editor", "Release"):
            for target in ("PrepareForBuild", "PrepareForPublish"):
                with self.subTest(configuration=configuration, target=target):
                    result = self.run_target(target, f"-p:Configuration={configuration}")
                    self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_incompatible_settings_fail_before_build_and_publish(self):
        for property_name in ("PublishAot", "PublishTrimmed", "PublishSingleFile"):
            for target in ("PrepareForBuild", "PrepareForPublish"):
                with self.subTest(property_name=property_name, target=target):
                    result = self.run_target(target, f"-p:{property_name}=true")
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("requires a JIT runtime and assembly metadata on disk", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
