#!/usr/bin/env python3
"""Run Godot's resource import pipeline against a project."""

import os
import platform
import subprocess
import sys


def detect_editor_binary():
    """Find the Godot editor binary in godot/bin/."""
    system = platform.system()
    machine = platform.machine().lower()

    arch = "x86_64"
    if machine in ("arm64", "aarch64"):
        arch = "arm64"

    if system == "Windows":
        name = f"godot.windows.editor.{arch}.executable.mono.exe"
    elif system == "Linux":
        name = f"godot.linuxbsd.editor.{arch}.executable.mono"
    elif system == "Darwin":
        name = f"godot.macos.editor.{arch}.executable.mono"
    else:
        print(f"Unsupported platform: {system}", file=sys.stderr)
        sys.exit(1)

    path = os.path.join("godot", "bin", name)
    if not os.path.exists(path):
        print(f"Editor binary not found: {path}", file=sys.stderr)
        print("Build the editor first: uv run poe build-godot", file=sys.stderr)
        sys.exit(1)

    return path


def main():
    project = sys.argv[1] if len(sys.argv) > 1 else "./game"
    editor = os.environ.get("GODOT_EDITOR") or detect_editor_binary()

    project = os.path.abspath(project)
    if not os.path.isfile(os.path.join(project, "project.godot")):
        print(f"No project.godot found in: {project}", file=sys.stderr)
        sys.exit(1)

    result = subprocess.run([editor, "--headless", "--import", "--path", project])
    sys.exit(result.returncode)


if __name__ == "__main__":
    main()
