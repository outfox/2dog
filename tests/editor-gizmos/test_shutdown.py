"""Run the C# gizmo shutdown regression with a built Godot editor."""
import argparse
from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[2]
PROJECT = Path(__file__).resolve().parent


def run(*command):
    result = subprocess.run(list(map(str, command)), cwd=ROOT, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
    if result.returncode:
        raise RuntimeError(f"Exit code {result.returncode}: {' '.join(map(str, command))}\n{result.stdout}")
    return result.stdout


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("editor", type=Path, help="Path to the built Godot editor executable")
    args = parser.parse_args()
    editor = args.editor.resolve()
    run("dotnet", "build", PROJECT, "--nologo")
    run(editor, "--headless", "--editor", "--path", PROJECT, "--import")
    output = run(editor, "--headless", "--editor", "--path", PROJECT,
                 "res://main.tscn", "--quit-after", "180")
    expected = "Gizmo regression: 100 gizmos created."
    if expected not in output:
        raise RuntimeError(f"The fixture did not exercise all 100 gizmos.\n{output}")
    print(expected + " Editor exited cleanly.")
