"""Execute the real Poe task graph with recording commands instead of compilers."""
import io
import json
import os
from pathlib import Path
import tempfile
import tomllib
import unittest
from unittest.mock import patch

from poethepoet.app import PoeThePoet

ROOT = Path(__file__).resolve().parents[2]


class PoeBuildTests(unittest.TestCase):
    def run_task(self, task, precision, *arguments):
        with tempfile.TemporaryDirectory(prefix="2dog-poe-") as directory:
            root = Path(directory)
            recorder = root / "record.py"
            recorder.write_text('''import json, os, subprocess, sys
from pathlib import Path
record = dict(command=sys.argv[1], args=sys.argv[2:], precision=os.environ.get("TwoDogPrecision"))
if record["command"].startswith("python scripts/dotnet_build.py pack platforms"):
    record["package_id"] = subprocess.check_output(["dotnet", "msbuild", os.environ["TWODOG_POE_TEST_PROJECT"], "-getProperty:PackageId"], text=True).strip()
with Path(os.environ["TWODOG_POE_TEST_LOG"]).open("a") as output:
    output.write(json.dumps(record) + "\\n")
''')
            config = tomllib.loads((ROOT / "pyproject.toml").read_text())
            config["tool"]["poe"]["executor"] = {"type": "simple"}

            def replace_commands(definition):
                if "cmd" in definition:
                    command = definition["cmd"]
                    extra = " ${POE_EXTRA_ARGS}" if "${POE_EXTRA_ARGS}" in command else ""
                    definition["cmd"] = f'python "{recorder.as_posix()}" "{command.split(" ${POE_EXTRA_ARGS}")[0]}"{extra}'
                for child in definition.get("sequence", []):
                    if isinstance(child, dict):
                        replace_commands(child)

            for definition in config["tool"]["poe"]["tasks"].values():
                if isinstance(definition, dict):
                    replace_commands(definition)
            config_path = root / "poe_tasks.json"
            config_path.write_text(json.dumps(config["tool"]["poe"]))
            log = root / "commands.jsonl"
            with patch.dict(os.environ, {"TWODOG_POE_TEST_LOG": str(log), "TwoDogPrecision": "opposite",
                    "TWODOG_POE_TEST_PROJECT": str(ROOT / "platforms/twodog.win-x64/twodog.win-x64.csproj")}):
                app = PoeThePoet(cwd=root, config_name=config_path.name, output=io.StringIO())
                self.assertEqual(0, app([task, "--precision", precision, *arguments]))
            return [json.loads(line) for line in log.read_text().splitlines()]

    def test_precision_reaches_every_pack_and_build_command(self):
        for task in ("build", "build-managed", "pack-android", "pack-ios", "build-engine-configurations"):
            for precision in ("single", "double"):
                with self.subTest(task=task, precision=precision):
                    commands = self.run_task(task, precision)
                    self.assertTrue(commands)
                    self.assertEqual({precision}, {command["precision"] for command in commands})
                    if task == "build":
                        self.assertEqual("2dog.win-x64" + (".double" if precision == "double" else ""), commands[0]["package_id"])

    def test_native_flags_do_not_leak_into_the_managed_pack_sequence(self):
        commands = self.run_task("build-all", "double", "--", "--debug-symbols", "yes")
        self.assertIn("--debug-symbols", commands[0]["args"])
        self.assertTrue(all(not command["args"] for command in commands[1:]))


if __name__ == "__main__":
    unittest.main()
