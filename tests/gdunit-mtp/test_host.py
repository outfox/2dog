"""Real-engine contracts for the packed gdUnit MTP host. Run with uv run python."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
import uuid
import zipfile
from xml.etree import ElementTree

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
FIXTURES = HERE.parent / "gdscript-host" / "fixtures"
CONFIGURATION = "Debug"
PRECISION = os.environ.get("TwoDogPrecision", "single") or "single"


def fingerprint(root):
    return {
        str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in root.rglob("*")
        if path.is_file()
    }


class GdUnitHostTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.environment = {**os.environ, "TwoDogPrecision": PRECISION}
        for host in ("pure", "mixed"):
            result = subprocess.run(
                ["dotnet", "build", str(HERE / host / f"gdunit.{host}.csproj"), "-c", CONFIGURATION],
                cwd=HERE, env=cls.environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                encoding="utf-8", errors="replace", timeout=180,
            )
            if result.returncode:
                raise AssertionError(result.stdout)
        cls.artifacts = HERE / "bin" / "contracts" / f"{CONFIGURATION}-{PRECISION}-{uuid.uuid4().hex[:12]}"
        cls.artifacts.mkdir(parents=True)
        print(f"gdUnit contract artifacts: {cls.artifacts}", flush=True)

    def setUp(self):
        self.output = self.artifacts / hashlib.sha256(self.id().encode()).hexdigest()[:8]
        self.output.mkdir()
        (self.output / "test-name.txt").write_text(self.id(), encoding="utf-8")
        self.before = {name: fingerprint(FIXTURES / name) for name in ("pure", "mixed")}

    def tearDown(self):
        for name, before in self.before.items():
            self.assertEqual(before, fingerprint(FIXTURES / name), f"Source {name} was modified")

    def run_host(self, *arguments, host="pure", project=None, roots=None, timeout_ms=None, dotnet_test=False):
        output = self.output / uuid.uuid4().hex[:8]
        environment = {**self.environment, "GDUNIT_TEST_ARTIFACTS": str(output)}
        if project:
            environment["GDUNIT_TEST_PROJECT"] = str(project)
        if roots:
            environment["GDUNIT_TEST_ROOTS"] = roots
        if timeout_ms:
            environment["GDUNIT_TEST_TIMEOUT_MS"] = str(timeout_ms)
        binary = HERE / host / "bin"
        if PRECISION == "double":
            binary /= "double"
        binary = binary / CONFIGURATION / "net10.0" / f"gdunit.{host}.dll"
        command = (["dotnet", "test", "--project", str(HERE / host / f"gdunit.{host}.csproj"),
                    "-c", CONFIGURATION, "--no-build"] if dotnet_test else ["dotnet", str(binary)])
        result = subprocess.run(
            [*command, *arguments], cwd=HERE, env=environment, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding="utf-8", errors="replace", timeout=90,
        )
        output.mkdir(exist_ok=True)
        (output / "console.log").write_text(result.stdout, encoding="utf-8")
        event_files = list(output.glob("*/events.json"))
        events = json.loads(event_files[0].read_text(encoding="utf-8")) if event_files else []
        stage = event_files[0].parent / "project" if event_files else None
        return result, events, stage

    def assert_exit(self, result, code):
        self.assertEqual(code, result.returncode, result.stdout)

    @staticmethod
    def terminal(events):
        return [item["event"] for item in events if item["kind"] == "event" and item["event"]["type"] == 5]

    @staticmethod
    def state(events):
        return next(item["fixture_state"] for item in events if item["kind"] == "completed")

    def discover(self):
        result, events, _ = self.run_host("--list-tests", "json")
        self.assert_exit(result, 0)
        # Native stdout can flush before or after MTP's JSON document, especially in Release on Unix.
        document, _ = json.JSONDecoder().raw_decode(result.stdout, result.stdout.index('{\n  "schemaVersion"'))
        self.assertEqual(5, len(document["tests"]))
        self.assertEqual([], self.terminal(events))
        self.assertEqual(0, self.state(events)["test_bodies"])
        self.assertEqual(0, self.state(events)["setups"])
        return document["tests"]

    def test_discovery_ids_and_source_locations_are_stable(self):
        first = self.discover()
        second = self.discover()
        self.assertEqual(first, second)
        self.assertEqual(5, len({test["uid"] for test in first}))
        for test in first:
            location = test["location"]
            path = Path(location["file"])
            self.assertTrue(path.is_relative_to(FIXTURES / "pure"))
            self.assertTrue(path.read_text().splitlines()[location["lineStart"] - 1].startswith("func test_"))

    def test_uid_selection_executes_one_parameter_row(self):
        tests = self.discover()
        row = next(test for test in tests if "parameter:1" in test["displayName"])
        result, events, _ = self.run_host("--filter-uid", row["uid"])
        self.assert_exit(result, 0)
        self.assertEqual(1, len(self.terminal(events)))
        self.assertEqual(1, self.state(events)["test_bodies"])
        self.assertEqual(1, self.state(events)["setups"])
        self.assertEqual(1, self.state(events)["teardowns"])
        self.assertTrue(self.state(events)["suite_finished"])

    def test_assertions_and_skips_set_mtp_outcomes(self):
        result, events, _ = self.run_host("--output", "Detailed")
        self.assert_exit(result, 2)
        self.assertEqual(5, len(self.terminal(events)))
        self.assertIn("failed: 1", result.stdout)
        self.assertIn("succeeded: 3", result.stdout)
        self.assertIn("skipped: 1", result.stdout)
        self.assertIn("core_test.gd:33", result.stdout)
        self.assertEqual(4, self.state(events)["test_bodies"])

    def test_dotnet_test_name_filter_and_empty_selection(self):
        result, events, _ = self.run_host("--gdunit-filter", "*test_async_parameter*", dotnet_test=True)
        self.assert_exit(result, 0)
        self.assertEqual(2, len(self.terminal(events)))
        result, events, _ = self.run_host("--filter-uid", "does-not-exist")
        self.assert_exit(result, 8)
        self.assertEqual([], self.terminal(events))
        self.assertEqual(0, self.state(events)["test_bodies"])

    def test_mixed_scene_loads_game_assembly(self):
        result, events, _ = self.run_host(host="mixed")
        self.assert_exit(result, 0)
        self.assertEqual(1, len(self.terminal(events)))

    def project(self, script):
        project = self.output / "game"
        (project / "tests").mkdir(parents=True)
        (project / "project.godot").write_text('config_version=5\n[application]\nconfig/name="Host contracts"\n', encoding="utf-8")
        (project / "tests" / "contract_test.gd").write_text(script, encoding="utf-8")
        return project

    def test_suite_teardown_failure_fails_the_host(self):
        script = ('extends GdUnitTestSuite\n'
                  'func test_pass() -> void:\n\tassert_bool(true).is_true()\n'
                  'func after() -> void:\n\tassert_int(1).is_equal(2)\n')
        project = self.project(script)
        (project / "tests" / "another_test.gd").write_text(script, encoding="utf-8")
        before = fingerprint(project)
        result, events, _ = self.run_host(project=project, roots="tests")
        self.assert_exit(result, 2)
        self.assertIn("gdUnit host", result.stdout)
        self.assertEqual(2, len(self.terminal(events)))
        self.assertIn("failed: 2", result.stdout)
        self.assertEqual(before, fingerprint(project))

    def test_bad_script_and_escaping_test_root_fail(self):
        project = self.project("this is deliberately invalid GDScript\n")
        before = fingerprint(project)
        result, _, _ = self.run_host(project=project, roots="tests")
        self.assert_exit(result, 2)
        self.assertRegex(result.stdout, "Godot import failed|gdUnit discovery failed")
        result, _, _ = self.run_host(project=project, roots="../outside")
        self.assert_exit(result, 2)
        self.assertIn("escapes the source project", result.stdout)
        self.assertEqual(before, fingerprint(project))

    def test_empty_project_uses_mtp_no_tests_policy(self):
        project = self.project("extends RefCounted\n")
        result, events, _ = self.run_host(project=project, roots="tests")
        self.assert_exit(result, 8)
        self.assertEqual([], self.terminal(events))

    def test_overlapping_roots_do_not_duplicate_tests(self):
        result, events, _ = self.run_host("--list-tests", roots="Pure.gdtest/tests;Pure.gdtest/tests;Pure.gdtest")
        self.assert_exit(result, 0)
        self.assertEqual(5, sum(item["kind"] == "discovered" for item in events))

    def test_cancellation_finishes_suite_teardown(self):
        project = self.project('extends GdUnitTestSuite\n'
                               'func test_wait(_timeout := 60000) -> void:\n'
                               '\tFileAccess.open("res://twodog_test_host/started.txt", FileAccess.WRITE).store_string("started")\n'
                               '\tawait get_tree().create_timer(60).timeout\n'
                               'func after() -> void:\n'
                               '\tFileAccess.open("res://twodog_test_host/teardown.txt", FileAccess.WRITE).store_string("done")\n')
        before = fingerprint(project)
        result, events, stage = self.run_host("--timeout", "30s", project=project, roots="tests")
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIsNotNone(stage, result.stdout)
        self.assertTrue((stage / "twodog_test_host" / "started.txt").exists(), result.stdout)
        self.assertTrue((stage / "twodog_test_host" / "teardown.txt").exists(), result.stdout)
        self.assertTrue(any(item["kind"] == "completed" for item in events), result.stdout)
        self.assertEqual(before, fingerprint(project))

    def test_pack_contains_runtime_templates_license_and_no_self_tests(self):
        suffix = ".double" if PRECISION == "double" else ""
        props = ElementTree.parse(REPO / "Directory.Build.props").getroot()
        godot_version = props.findtext(".//GodotVersion")
        revision = props.findtext(".//TwoDogRevision")
        package = REPO / "packages" / f"2dog.gdunit{suffix}.{godot_version}.{revision}.nupkg"
        with zipfile.ZipFile(package) as archive:
            names = archive.namelist()
            for name in ("runtime/.gdignore", "runtime/bridge/runner.tscn", "runtime/addons/gdUnit4/LICENSE",
                         "runtime/addons/gdUnit4/src/reporters/html/template/.gdignore"):
                self.assertIn(name, names)
            self.assertFalse(any(name.startswith("runtime/addons/gdUnit4/test/") for name in names))
            pin = ElementTree.parse(REPO / "twodog.gdunit" / "GdUnitRuntime.props").getroot()
            version = pin.findtext(".//GdUnitVersion")
            metadata = json.loads(archive.read("runtime/gdunit-release.json").decode("utf-8-sig"))
            self.assertEqual({
                "version": version,
                "archiveUrl": pin.findtext(".//GdUnitArchiveUrl").replace("$(GdUnitVersion)", version),
                "sha256": pin.findtext(".//GdUnitArchiveSha256"),
            }, metadata)
            plugin = archive.read("runtime/addons/gdUnit4/plugin.cfg").decode("utf-8")
            self.assertIn(f'version="{version}"', plugin)

    def test_corrupted_official_archive_is_rejected_before_extraction(self):
        props = ElementTree.parse(REPO / "twodog.gdunit" / "GdUnitRuntime.props").getroot()
        version = props.findtext(".//GdUnitVersion")
        cache = self.output / "release-cache"
        cache.mkdir()
        (cache / f"GdUnit.{version}.zip").write_bytes(b"not the official release archive")
        result = subprocess.run(
            ["dotnet", "msbuild", str(REPO / "twodog.gdunit" / "twodog.gdunit.csproj"),
             "-t:PrepareGdUnitRuntime", f"-p:_GdUnitCacheDirectory={cache}",
             f"-p:TwoDogPrecision={PRECISION}"],
            cwd=REPO, env=self.environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            encoding="utf-8", errors="replace", timeout=90,
        )
        (self.output / "pack.log").write_text(result.stdout, encoding="utf-8")
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIn("GDUNIT001", result.stdout)
        self.assertFalse((cache / "unpacked").exists(), result.stdout)

    def test_documented_host_builds_outside_repository(self):
        # Keep the consumer for diagnosis. It inherits no repository props, targets, SDK source or package cache.
        consumer = Path(tempfile.mkdtemp(prefix="2dog-gdunit-"))
        (self.output / "consumer-path.txt").write_text(str(consumer), encoding="utf-8")
        host = consumer / "MyGame.gdunit"
        (host / "tests").mkdir(parents=True)
        (host / ".gdignore").touch()
        (consumer / "project.godot").write_text('config_version=5\n[application]\nconfig/name="Consumer"\n', encoding="utf-8")
        readme = (REPO / "twodog.gdunit" / "README.md").read_text(encoding="utf-8")
        props = ElementTree.parse(REPO / "Directory.Build.props").getroot()
        version = f'{props.findtext(".//GodotVersion")}.{props.findtext(".//TwoDogRevision")}'
        project = re.search(r"```xml\n(.*?)```", readme, re.S).group(1).replace("YOUR_2DOG_VERSION", version)
        if PRECISION == "double":
            project = project.replace('Include="2dog.gdunit"', 'Include="2dog.gdunit.double"')
        (host / "MyGame.gdunit.csproj").write_text(project, encoding="utf-8")
        (host / "Program.cs").write_text(re.search(r"```csharp\n(.*?)```", readme, re.S).group(1), encoding="utf-8")
        (host / "global.json").write_text(re.search(r"```json\n(.*?)```", readme, re.S).group(1), encoding="utf-8")
        (host / "tests" / "example_test.gd").write_text(re.search(r"```gdscript\n(.*?)```", readme, re.S).group(1), encoding="utf-8")
        config = ElementTree.Element("configuration")
        settings = ElementTree.SubElement(config, "config")
        ElementTree.SubElement(settings, "add", key="globalPackagesFolder", value=str(consumer / ".packages"))
        sources = ElementTree.SubElement(config, "packageSources")
        ElementTree.SubElement(sources, "clear")
        ElementTree.SubElement(sources, "add", key="packed", value=str(REPO / "packages"))
        ElementTree.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        ElementTree.ElementTree(config).write(consumer / "nuget.config", encoding="utf-8", xml_declaration=True)
        result = subprocess.run(
            ["dotnet", "test", "--project", "MyGame.gdunit.csproj", "-c", CONFIGURATION],
            cwd=host, env=self.environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            encoding="utf-8", errors="replace", timeout=180,
        )
        (self.output / "consumer.log").write_text(result.stdout, encoding="utf-8")
        self.assert_exit(result, 0)
        self.assertIn("succeeded: 1", result.stdout)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--configuration", choices=("Debug", "Release", "Editor"), default="Debug")
    parser.add_argument("--precision", choices=("single", "double"), default=PRECISION)
    options, remaining = parser.parse_known_args()
    CONFIGURATION = options.configuration
    PRECISION = options.precision
    unittest.main(argv=[sys.argv[0], *remaining], verbosity=2)
