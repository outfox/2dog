"""Require at least 95% lines and branches in the whole packaged REPL host."""

import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def check(report: Path) -> str:
    reports = list(report.rglob("coverage.cobertura.xml")) if report.is_dir() else [report]
    if len(reports) != 1:
        raise ValueError(f"Expected exactly one coverage report in {report}; found {len(reports)}. Use a fresh results directory.")
    root = ET.parse(reports[0]).getroot()
    packages = root.findall("./packages/package")
    if root.tag != "coverage" or [package.get("name") for package in packages] != ["twodog.repl"]:
        raise ValueError("Expected coverage of only the twodog.repl assembly. Use tests/repl/coverage.runsettings.")
    totals = []
    failures = []
    for metric in ("lines", "branches"):
        covered, valid = (int(root.attrib[f"{metric}-{key}"]) for key in ("covered", "valid"))
        if not 0 <= covered <= valid or valid == 0:
            raise ValueError(f"Invalid or empty {metric} coverage: {covered}/{valid}.")
        totals.append(f"{metric}: {covered}/{valid} ({100 * covered / valid:.2f}%)")
        # Compare counts, not rounded percentages: 94.999% must fail.
        if 100 * covered < 95 * valid:
            failures.append(metric)
    summary = "twodog.repl " + "; ".join(totals)
    if failures:
        raise ValueError(summary + "; below 95% for " + ", ".join(failures))
    return summary


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path, help="Cobertura XML or a fresh results directory containing exactly one report")
    args = parser.parse_args()
    try:
        print(check(args.report))
        return 0
    except (OSError, ET.ParseError, KeyError, ValueError) as error:
        print(f"REPL coverage failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
