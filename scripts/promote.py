#!/usr/bin/env python3
"""Promote tested release tags from the test feed to nuget.org.

Dispatches publish.yml once per tag (nuget.org's Trusted Publishing policy names that workflow, so the push itself
must run there). Every tag is checked to have a GitHub release before any workflow is dispatched.

Usage:
  uv run poe promote v4.7.2.189 v4.8.0.188-beta3
"""

import argparse
import re
import subprocess
import sys

REPO = "outfox/2dog"
TAG = re.compile(r"^v\d+\.\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")


def gh(*args: str) -> subprocess.CompletedProcess:
    return subprocess.run(["gh", *args, "--repo", REPO], capture_output=True, text=True)


def main() -> int:
    parser = argparse.ArgumentParser(description="Promote release tags to nuget.org via publish.yml.")
    parser.add_argument("tags", nargs="+", metavar="tag", help="release tag, e.g. v4.7.2.189")
    tags = list(dict.fromkeys(parser.parse_args().tags))

    errors = [f"{tag}: not a release tag (vMAJOR.MINOR.PATCH.REVISION[-label])" for tag in tags if not TAG.match(tag)]
    errors += [
        f"{tag}: no GitHub release ({result.stderr.strip()})"
        for tag in tags
        if TAG.match(tag) and (result := gh("release", "view", tag, "--json", "tagName")).returncode != 0
    ]
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1

    for tag in tags:
        result = gh("workflow", "run", "publish.yml", "-f", f"tag={tag}", "-f", "feed=nuget")
        if result.returncode != 0:
            print(f"{tag}: dispatch failed: {result.stderr.strip()}", file=sys.stderr)
            return 1
        print(f"{tag}: promoting to nuget.org {result.stdout.strip()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
