"""Identify committed native build inputs, independently of release counters."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
BUILD_INPUTS = (
    "build-godot.py", "scripts/native_inputs.py", "scripts/webgpu.py", "scripts/dotnet_build.py",
    "scripts/build_godot_assemblies.py",
    "scripts/build_android_java.py", "nuget/webgpu",
    ".github/workflows/build-natives.yml", "global.json", "pyproject.toml", "uv.lock",
)
PACKAGE_INPUTS = ("platforms", "nuget", "build", "Directory.Build.targets", "scripts/stage_natives_release.py")


def git(*args, root=ROOT):
    return subprocess.check_output([shutil.which("git") or "git", *args], cwd=root)


def fingerprint(ref="HEAD", godot=None, kind="build", root=ROOT):
    godot = godot or git("rev-parse", f"{ref}:godot", root=root).decode().strip()
    paths = (*BUILD_INPUTS, *(PACKAGE_INPUTS if kind == "package" else ()))
    tree = git("ls-tree", "-r", "--full-tree", ref, "--", *paths, root=root)
    props = git("show", f"{ref}:Directory.Build.props", root=root).decode()
    emscripten = ET.fromstring(props).findtext(".//EmscriptenVersion")
    if kind == "package":
        # Another line supplies its own gitlink; release counters and labels never change native bytes.
        props = re.sub(r"<(GodotVersion|TwoDogRevision|NativesRevision|TwoDogPrerelease)>[^<]*</\1>", "", props)
    else:
        props = emscripten or ""
    return hashlib.sha256(godot.encode() + b"\0" + tree + props.replace("\r\n", "\n").encode()).hexdigest()


def file_hash(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def release_manifest(assets, precision):
    identity = fingerprint()
    manifest = {"identity": identity, "godot": git("rev-parse", "HEAD:godot").decode().strip(),
                "precision": precision, "tag": f"godot-{identity}" + ("-double" if precision == "double" else ""),
                "assets": {p.name: file_hash(p) for p in sorted(assets.glob("*.zip"))}}
    (assets / "native-build.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ref", default="HEAD")
    parser.add_argument("--godot")
    parser.add_argument("--kind", choices=("build", "package"), default="build")
    parser.add_argument("--assets", type=Path)
    parser.add_argument("--precision", choices=("single", "double"), default="single")
    args = parser.parse_args()
    if args.assets:
        release_manifest(args.assets, args.precision)
    else:
        print(fingerprint(args.ref, args.godot, args.kind))
