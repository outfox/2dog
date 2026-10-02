#!/usr/bin/env python3
"""Build Godot's Java host once per variant, without replacing the fork's native builds."""
import argparse
import os
from pathlib import Path
import subprocess
import zipfile

REPO = Path(__file__).resolve().parents[1]


def strip_native_libraries(source: Path, destination: Path):
    """Native libraries come from RID packages; retaining AAR natives duplicates/mixes variants."""
    with zipfile.ZipFile(source) as archive:
        names = archive.namelist()
        if "classes.jar" not in names or "AndroidManifest.xml" not in names:
            raise ValueError(f"Not a Godot Android library: {source}")
        destination.parent.mkdir(parents=True, exist_ok=True)
        temporary = destination.with_suffix(".tmp")
        try:
            with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as output:
                for entry in archive.infolist():
                    if not entry.filename.startswith("jni/"):
                        output.writestr(entry, archive.read(entry))
            temporary.replace(destination)
        finally:
            temporary.unlink(missing_ok=True)


def gradle_command(java_dir: Path, variant: str):
    wrapper = java_dir / ("gradlew.bat" if os.name == "nt" else "gradlew")
    command = [str(wrapper), f":lib:assembleTemplate{variant.capitalize()}", "--no-daemon"]
    # Direct assemble tasks do not use the root project's template exclusion hooks.
    # Its selectedAbis defaults to arm64. Exclude SCons so Gradle cannot overwrite mono builds.
    for build_type in ("Debug", "Release"):
        command += ["-x", f":lib:compileGodotNativeLibsTemplate{build_type}Arm64"]
    return (["cmd", "/c"] + command) if os.name == "nt" else command


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot-dir", type=Path, default=REPO / "godot")
    parser.add_argument("--variant", choices=("all", "debug", "release"), default="all")
    args = parser.parse_args()
    java_dir = args.godot_dir.resolve() / "platform/android/java"
    variants = ("debug", "release") if args.variant == "all" else (args.variant,)
    for variant in variants:
        subprocess.run(gradle_command(java_dir, variant), cwd=java_dir, check=True)
        source = java_dir / f"lib/build/outputs/aar/godot-lib.template_{variant}.aar"
        destination = args.godot_dir.resolve() / f"bin/android/java/{variant}/godot.aar"
        strip_native_libraries(source, destination)
        print(f"Staged {destination}")


if __name__ == "__main__":
    main()
