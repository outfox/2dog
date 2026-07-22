#!/usr/bin/env python3
"""Refresh gdextension/extension_api.json from a locally built editor binary.

Must be a non-mono build (extra_suffix=gdext_executable): a mono editor would
leak module_mono classes (CSharpScript, ...) into the dump, and the dump is the
source of truth for the twodog.bindings method hashes.
"""

import argparse
import platform
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent


def find_editor() -> Path | None:
    plat = {"Windows": "windows", "Linux": "linuxbsd", "Darwin": "macos"}[platform.system()]
    ext = ".exe" if platform.system() == "Windows" else ""
    arch = platform.machine().lower()
    arch = {"amd64": "x86_64", "aarch64": "arm64"}.get(arch, arch)
    pattern = f"godot.{plat}.editor.{arch}.gdext_executable{ext}"
    return next(iter((REPO / "godot" / "bin").glob(pattern)), None)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--editor", type=Path, help="editor binary to dump from (must be a non-mono build)")
    args = parser.parse_args()

    editor = args.editor or find_editor()
    if editor is None or not editor.exists():
        sys.exit(
            "No non-mono editor binary in godot/bin. Build one with:\n"
            "  uv run build-godot.py --mono no --no-glue --no-library"
        )

    out_dir = REPO / "gdextension"
    # The engine writes extension_api.json into the current directory.
    subprocess.run([str(editor), "--headless", "--dump-extension-api"], cwd=out_dir, check=True)
    print(f"Wrote {out_dir / 'extension_api.json'} (from {editor.name})")


if __name__ == "__main__":
    main()
