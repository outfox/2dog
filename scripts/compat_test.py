#!/usr/bin/env python3
"""Drive the hash-compat check (twodog.bindings.compat) for both libgodot
flavors and enforce a clean boot: a non-zero exit OR any engine ERROR line
fails the flavor. This is what makes a PASS trustworthy - a binary that spews
errors while booting is not certified, even if every hash resolves."""

import re
import subprocess
import sys

ERROR_PATTERNS = (
    re.compile(r"^ERROR: "),
    re.compile(r"runtimeconfig\.json.*does not exist"),
)


def run_flavor(flavor: str) -> bool:
    proc = subprocess.run(
        ["dotnet", "run", "--project", "twodog.bindings.compat", "--", "--flavor", flavor],
        capture_output=True,
        text=True,
    )
    sys.stdout.write(proc.stdout)
    sys.stderr.write(proc.stderr)
    error_lines = [
        line
        for line in (proc.stdout + proc.stderr).splitlines()
        if any(p.search(line) for p in ERROR_PATTERNS)
    ]
    ok = proc.returncode == 0 and not error_lines
    if error_lines:
        print(f"[compat-driver] {flavor}: {len(error_lines)} engine error line(s) during boot:")
        for line in error_lines:
            print(f"[compat-driver]   {line}")
    print(f"[compat-driver] {flavor}: {'PASS' if ok else 'FAIL'} (exit {proc.returncode})")
    return ok


if __name__ == "__main__":
    # Evaluate both flavors even if the first fails - full report, single verdict.
    results = [run_flavor("mono"), run_flavor("gdext")]
    sys.exit(0 if all(results) else 1)
