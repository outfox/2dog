"""Verify Windows process exit with an ordinary or retained GDExtension DLL."""
import argparse
from pathlib import Path
import subprocess
import unittest

import test_hosts


HERE = Path(__file__).resolve().parent


class ExtensionShutdownContracts(test_hosts.ManagedMtpContracts):
    HOST = "xunit"

    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        build = cls.output / "native"
        build.mkdir()
        # Native MSBuild projects must not import the repository's .NET packaging targets.
        for name in ("Directory.Build.props", "Directory.Build.targets"):
            (build / name).write_text("<Project/>", encoding="utf-8")
        for command in (("cmake", "-S", str(HERE / "extension"), "-B", str(build)),
                        ("cmake", "--build", str(build), "--config", "Release")):
            result = subprocess.run(command, capture_output=True, text=True, timeout=120)
            if result.returncode:
                raise AssertionError(result.stdout + result.stderr)
        library, = build.rglob("shutdown_probe.dll")
        extension = cls.output / "shutdown_probe.gdextension"
        extension.write_text('[configuration]\nentry_symbol="shutdown_probe_init"\ncompatibility_minimum="4.1"\n'
                             f'[libraries]\nwindows.x86_64="{library.as_posix()}"\n', encoding="utf-8")
        cls.extension = extension
        cls.library = library

    def test_extension_destructor_can_call_godot_after_fixture_teardown(self):
        test = next(test for test in self.discover() if test["displayName"].endswith("Passing"))
        for retained in ("normal", "exit-handler", "process-detach"):
            with self.subTest(retained=retained):
                marker = self.output / f"extension-{retained}.events"
                self.environment = {**self.environment, "MTP_CONTRACT_EXTENSION": str(self.extension),
                                    "MTP_CONTRACT_EXTENSION_LIBRARY": str(self.library),
                                    "MTP_CONTRACT_EXTENSION_LOG": str(marker),
                                    "MTP_CONTRACT_RETAIN_EXTENSION": retained}
                result, events = self.run_host("--filter-uid", test["uid"], dotnet_test=True)
                self.assertEqual(0, result.returncode, result.stdout)
                self.assert_owner_thread_cleanup(events)
                steps = marker.read_text().splitlines()
                self.assertIn("initialize:2", steps)
                self.assertEqual(["deinitialize:2", "deinitialize:1", "deinitialize:0",
                                  "static-destructor-enter:-1", "static-destructor:-1"], steps[-5:])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", choices=("Debug", "Release", "Editor"), default="Release")
    parser.add_argument("--precision", choices=("single", "double"), default=test_hosts.PRECISION)
    options = parser.parse_args()
    test_hosts.CONFIGURATION, test_hosts.PRECISION = options.configuration, options.precision
    suite = unittest.TestSuite([ExtensionShutdownContracts(
        "test_extension_destructor_can_call_godot_after_fixture_teardown")])
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(0 if result.wasSuccessful() else 1)
