"""Check source-checkout helper discovery without loading a native engine."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]


class ImportHelperDiscoveryTests(unittest.TestCase):
    def test_source_helpers_follow_precision_and_do_not_fall_back_to_single(self):
        with tempfile.TemporaryDirectory(prefix="2dog-helper-discovery-") as scratch:
            root = Path(scratch)
            targets = root / "twodog.engine/build/2dog.engine.targets"
            targets.parent.mkdir(parents=True)
            shutil.copyfile(ROOT / "twodog.engine/build/2dog.engine.targets", targets)
            project = root / "consumer.proj"
            for subdir in ("", "double/"):
                for config in ("Debug", "Release"):
                    helper = root / f"twodog.import/bin/{subdir}{config}/net10.0/2dog.import.dll"
                    helper.parent.mkdir(parents=True)
                    helper.write_bytes(b"fixture")

            def resolve(subdir):
                project.write_text('<Project><PropertyGroup>'
                    f'<TwoDogNativeOutputSubdir>{subdir}</TwoDogNativeOutputSubdir>'
                    f'</PropertyGroup><Import Project="{escape(str(targets))}"/></Project>')
                result = subprocess.run(["dotnet", "msbuild", str(project), "-nologo",
                    "-getProperty:TwoDogImportHelperPath"], capture_output=True, text=True, timeout=60)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                return result.stdout.strip()

            for subdir in ("", "double/"):
                self.assertEqual((root / f"twodog.import/bin/{subdir}Release/net10.0/2dog.import.dll").resolve(),
                                 Path(resolve(subdir)).resolve())
            (root / "twodog.import/bin/double/Release/net10.0/2dog.import.dll").unlink()
            self.assertEqual((root / "twodog.import/bin/double/Debug/net10.0/2dog.import.dll").resolve(),
                             Path(resolve("double/")).resolve())
            (root / "twodog.import/bin/double/Debug/net10.0/2dog.import.dll").unlink()
            self.assertEqual("", resolve("double/"))


if __name__ == "__main__":
    unittest.main()
