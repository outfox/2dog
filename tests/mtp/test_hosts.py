"""MTP contracts for the packed xUnit, NUnit and GdUnit hosts."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
import uuid

HERE = Path(__file__).resolve().parent
CONFIGURATION = "Debug"
PRECISION = os.environ.get("TwoDogPrecision", "single") or "single"


class ManagedMtpContracts(unittest.TestCase):
    HOST = ""
    CANCELLATION_ARGUMENTS = ("--timeout", "10s")

    @classmethod
    def setUpClass(cls):
        cls.environment = {**os.environ, "TwoDogPrecision": PRECISION}
        cls.output = HERE / "bin" / f"{cls.HOST}-{CONFIGURATION}-{PRECISION}-{uuid.uuid4().hex[:8]}"
        cls.output.mkdir(parents=True)
        project = HERE / cls.HOST / f"mtp.{cls.HOST}.csproj"
        result = subprocess.run(["dotnet", "build", str(project), "-c", CONFIGURATION],
                                cwd=HERE, env=cls.environment, text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, encoding="utf-8", errors="replace", timeout=180)
        if result.returncode:
            raise AssertionError(result.stdout)
        binary = HERE / cls.HOST / "bin"
        if PRECISION == "double":
            binary /= "double"
        cls.binary = binary / CONFIGURATION / "net10.0" / f"mtp.{cls.HOST}.dll"

    def run_host(self, *arguments, wait=False, dotnet_test=False):
        output = self.output / uuid.uuid4().hex[:8]
        output.mkdir()
        marker = output / "fixture.events"
        environment = {**self.environment, "MTP_CONTRACT_LOG": str(marker),
                       "MTP_CONTRACT_WAIT": "1" if wait else "0",
                       "TWODOG_GODOT_LOG_DIR": str(output / "godot")}
        command = (["dotnet", "test", "--project", str(HERE / self.HOST / f"mtp.{self.HOST}.csproj"),
                    "-c", CONFIGURATION, "--no-build"] if dotnet_test else ["dotnet", str(self.binary)])
        result = subprocess.run([*command, *arguments], cwd=HERE, env=environment, text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding="utf-8",
                                errors="replace", timeout=90)
        (output / "console.log").write_text(result.stdout, encoding="utf-8")
        events = [line.split("|") for line in marker.read_text().splitlines()] if marker.exists() else []
        return result, events

    def discover(self):
        result, events = self.run_host("--list-tests", "json")
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual([], events, "Discovery initialized a fixture or ran a test")
        document = json.loads(result.stdout[result.stdout.index('{\n  "schemaVersion"'):])
        self.assertEqual(7, len(document["tests"]), result.stdout)
        return document["tests"]

    def assert_owner_thread_cleanup(self, events):
        self.assertEqual("setup", events[0][0], events)
        self.assertEqual("teardown", events[-1][0], events)
        self.assertEqual(1, len({thread for _, thread in events}), events)

    def test_discovery_returns_stable_distinct_ids_without_starting_godot(self):
        first = self.discover()
        self.assertEqual(first, self.discover())
        self.assertEqual(7, len({test["uid"] for test in first}))

    def test_uid_selects_one_parameter_row_and_keeps_owner_thread(self):
        row = next(test for test in self.discover() if "Parameterized" in test["displayName"]
                   and ("value: 3" in test["displayName"] or "(3,6)" in test["displayName"]))
        result, events = self.run_host("--filter-uid", row["uid"])
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual(["setup", "Parameterized:3", "teardown"], [step for step, _ in events], result.stdout)
        self.assert_owner_thread_cleanup(events)

    def test_outcomes_include_native_warning_failures(self):
        result, events = self.run_host("--output", "Detailed")
        self.assertEqual(2, result.returncode, result.stdout)
        for summary in ("total: 7", "failed: 2", "succeeded: 4", "skipped: 1"):
            self.assertIn(summary, result.stdout)
        self.assertIn("MTP native warning contract", result.stdout)
        self.assert_owner_thread_cleanup(events)

    def test_empty_uid_selection_does_not_initialize_godot(self):
        result, events = self.run_host("--filter-uid", "does-not-exist")
        self.assertEqual(8, result.returncode, result.stdout)
        self.assertEqual([], events)

    def test_native_dotnet_test_uses_the_mtp_contract(self):
        test = next(test for test in self.discover() if test["displayName"].endswith("Passing"))
        result, events = self.run_host("--filter-uid", test["uid"], dotnet_test=True)
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertEqual(["setup", "Passing", "teardown"], [step for step, _ in events], result.stdout)
        self.assert_owner_thread_cleanup(events)

    def test_cooperative_cancellation_completes_owner_thread_teardown(self):
        test = next(test for test in self.discover() if test["displayName"].endswith("Cancellation"))
        result, events = self.run_host("--filter-uid", test["uid"], *self.CANCELLATION_ARGUMENTS, wait=True)
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertEqual(["setup", "Cancellation", "cancellation-observed", "teardown"], [step for step, _ in events], result.stdout)
        self.assert_owner_thread_cleanup(events)


class XunitMtpContracts(ManagedMtpContracts):
    HOST = "xunit"


class NUnitMtpContracts(ManagedMtpContracts):
    HOST = "nunit"
    # Upstream forced session stop skips NUnit teardown; use its cooperative test timeout.
    CANCELLATION_ARGUMENTS = ()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--configuration", choices=("Debug", "Release", "Editor"), default="Debug")
    parser.add_argument("--precision", choices=("single", "double"), default=PRECISION)
    parser.add_argument("--managed-only", action="store_true")
    options = parser.parse_args()
    CONFIGURATION, PRECISION = options.configuration, options.precision
    loader = unittest.TestLoader()
    suite = unittest.TestSuite([loader.loadTestsFromTestCase(XunitMtpContracts),
                               loader.loadTestsFromTestCase(NUnitMtpContracts)])
    if not options.managed_only:
        path = HERE.parent / "gdunit-mtp" / "test_host.py"
        spec = importlib.util.spec_from_file_location("gdunit_contracts", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.CONFIGURATION, module.PRECISION = CONFIGURATION, PRECISION
        suite.addTests(loader.loadTestsFromTestCase(module.GdUnitHostTests))
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    sys.exit(0 if result.wasSuccessful() else 1)
