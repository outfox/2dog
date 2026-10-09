"""Guard against green coverage checks with missing, stale or rounded data."""

import importlib.util
from pathlib import Path
import tempfile
import unittest


spec = importlib.util.spec_from_file_location("check_repl_coverage", Path(__file__).resolve().parents[2] / "scripts/check_repl_coverage.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class CoverageGateTests(unittest.TestCase):
    def setUp(self):
        scratch = tempfile.TemporaryDirectory()
        self.addCleanup(scratch.cleanup)
        self.directory = Path(scratch.name)
        self.report = self.directory / "coverage.cobertura.xml"

    def write(self, lines=95, branches=95, valid=100, package="twodog.repl"):
        self.report.write_text(f'<coverage lines-covered="{lines}" lines-valid="{valid}" branches-covered="{branches}" branches-valid="{valid}"><packages><package name="{package}"/></packages></coverage>')

    def test_exact_threshold_passes_for_both_metrics(self):
        self.write()
        self.assertIn("branches: 95/100 (95.00%)", gate.check(self.directory))

    def test_each_metric_is_required_independently(self):
        for lines, branches in ((94, 100), (100, 94)):
            with self.subTest(lines=lines, branches=branches):
                self.write(lines, branches)
                with self.assertRaisesRegex(ValueError, "below 95%"):
                    gate.check(self.report)

    def test_rounding_cannot_turn_failure_into_success(self):
        self.write(94999, 100000, valid=100000)
        with self.assertRaisesRegex(ValueError, "below 95% for lines"):
            gate.check(self.report)

    def test_empty_invalid_or_wrong_assembly_reports_fail(self):
        for options in ({"valid": 0, "lines": 0, "branches": 0}, {"lines": 101}, {"branches": -1}, {"package": "another.assembly"}):
            with self.subTest(options=options):
                self.write(**options)
                with self.assertRaises(ValueError):
                    gate.check(self.report)

    def test_missing_and_duplicate_reports_fail(self):
        with self.assertRaisesRegex(ValueError, "found 0"):
            gate.check(self.directory)
        self.write()
        duplicate = self.directory / "old" / self.report.name
        duplicate.parent.mkdir()
        duplicate.write_bytes(self.report.read_bytes())
        with self.assertRaisesRegex(ValueError, "found 2"):
            gate.check(self.directory)

    def test_missing_counts_and_malformed_xml_fail(self):
        self.report.write_text('<coverage><packages><package name="twodog.repl"/></packages></coverage>')
        with self.assertRaises(KeyError):
            gate.check(self.report)
        self.report.write_text("not XML")
        with self.assertRaises(gate.ET.ParseError):
            gate.check(self.report)


if __name__ == "__main__":
    unittest.main()
