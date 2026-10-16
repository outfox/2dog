"""Download a tested CI package feed and matching Godot SDK/bindings for this checkout."""

import argparse
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[1]
NATIVE_ID = re.compile(r"2dog\.(?:win-|linux-|osx-|browser-|android(?:[.-]|$)|tools(?:\.|$))")
COMPONENT = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.+-]*\Z")


def command(*args, capture=False):
    args = list(map(str, args))
    if not capture:
        print("+ " + subprocess.list2cmdline(args), flush=True)
    return subprocess.run(args, cwd=ROOT, check=True, text=True,
                          stdout=subprocess.PIPE if capture else None).stdout


def api(repo, endpoint):
    return json.loads(command("gh", "api", f"repos/{repo}/{endpoint}", capture=True))


def versions(path):
    props = ET.parse(path).getroot()
    godot = props.findtext(".//GodotVersion")
    managed = f"{godot}.{props.findtext('.//TwoDogRevision')}"
    native = f"{godot}.{props.findtext('.//NativesRevision')}"
    return godot, normalize_version(managed), normalize_version(native)


def normalize_version(value):
    # NuGet drops the fourth component when it is zero.
    return value[:-2] if re.fullmatch(r"\d+\.\d+\.\d+\.0", value) else value


def available_artifacts(artifacts, precisions):
    names = {a["name"] for a in artifacts if not a["expired"]}
    required = {f"nuget-packages-{p}" for p in precisions}
    return required.issubset(names)


def choose_run(repo, branch, run_id, precisions):
    if run_id:
        runs = [api(repo, f"actions/runs/{run_id}")]
    else:
        listing = json.loads(command("gh", "run", "list", "--repo", repo,
                                     "--workflow", "ci.yml", "--branch", branch,
                                     "--status", "success", "--limit", "30",
                                     "--json", "databaseId", capture=True))
        runs = (api(repo, f"actions/runs/{r['databaseId']}") for r in listing)
    for run in runs:
        if run["status"] != "completed" or run["conclusion"] != "success":
            raise ValueError(f"Run {run['id']} has not completed successfully")
        artifacts = api(repo, f"actions/runs/{run['id']}/artifacts?per_page=100")["artifacts"]
        if available_artifacts(artifacts, precisions):
            return run, artifacts
    raise ValueError("No successful CI run has the requested, unexpired NuGet artifacts. "
                     "Run CI on the branch again, or select another --run.")


@dataclass(frozen=True)
class Package:
    path: Path
    id: str
    version: str
    commit: str
    has_payload: bool


def inspect_package(path):
    with zipfile.ZipFile(path) as archive:
        nuspecs = [name for name in archive.namelist() if name.endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise ValueError(f"{path.name}: expected one nuspec")
        metadata = ET.fromstring(archive.read(nuspecs[0])).find("{*}metadata")
        package_id = metadata.findtext("{*}id")
        version = normalize_version(metadata.findtext("{*}version"))
        if not COMPONENT.fullmatch(package_id) or not COMPONENT.fullmatch(version):
            raise ValueError(f"{path.name}: unsafe package ID or version")
        repository = metadata.find("{*}repository")
        commit = repository.get("commit", "") if repository is not None else ""
        payload = any(p.file_size > 1_000_000 and (
            Path(p.filename).name.startswith("libgodot-")
            or p.filename.endswith("libgodot/libgodot.a")
            or (package_id.startswith("2dog.tools") and p.filename.endswith(".dll")))
            for p in archive.infolist())
        return Package(path, package_id.lower(), version, commit, payload)


def collect_packages(staging, precisions, expected, source_commit, native_commit):
    packages = {}
    for precision in precisions:
        for path in sorted((staging / f"nuget-packages-{precision}").glob("*.nupkg")):
            package = inspect_package(path)
            # Both artifacts contain neutral integrations. Keep the single-precision copy.
            if precision == "double" and "single" in precisions and not package.id.endswith(".double"):
                continue
            packages[package.id] = package
    for path in (staging / "nuget-packages-maui").glob("*.nupkg"):
        package = inspect_package(path)
        packages[package.id] = package
    for package in packages.values():
        native = bool(NATIVE_ID.match(package.id))
        version = expected[2 if native else 1]
        commit = native_commit if native else source_commit
        if not (package.id == "2dog" or package.id.startswith("2dog.")):
            raise ValueError(f"Unexpected package {package.id}")
        # Handwritten meta-package nuspecs omit repository metadata; payload packages carry it.
        if package.version != version or (package.commit and package.commit != commit):
            raise ValueError(f"{package.path.name}: version/source does not match this checkout's "
                             "versions and the selected CI run")
    for precision in precisions:
        suffix = ".double" if precision == "double" else ""
        required = {f"2dog.{name}{suffix}" for name in
                    ("engine", "godotsharp", "godotsharp.editor", "tools", "browser-wasm",
                     "win-x64", "linux-x64", "osx-arm64")}
        payloads = {f"2dog.{rid}.{variant}{suffix}" for rid in ("win-x64", "linux-x64", "osx-arm64")
                    for variant in ("debug", "release", "editor")}
        payloads |= {f"2dog.browser-wasm.{variant}{suffix}" for variant in ("debug", "release")}
        required |= payloads
        missing = required - packages.keys()
        if missing:
            raise ValueError(f"Incomplete {precision} feed: {', '.join(sorted(missing))}")
        for package_id in payloads | {f"2dog.tools{suffix}"}:
            if not packages[package_id].has_payload:
                raise ValueError(f"{package_id}: native payload is missing (local stub)")
    return list(packages.values())


def extract_checked(path, destination, required):
    with zipfile.ZipFile(path) as archive:
        members = archive.namelist()
        for pattern in required:
            if not any(Path(name).match(pattern) for name in members):
                raise ValueError(f"{path.name}: missing {pattern}")
        for member in members:
            target = (destination / member).resolve()
            if not target.is_relative_to(destination.resolve()):
                raise ValueError(f"Unsafe archive path: {member}")
        archive.extractall(destination)


def copy_file(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    # Replace on the destination's filesystem, including across mounted drives.
    temporary = target.with_name(target.name + ".ci-download")
    shutil.copyfile(source, temporary)
    temporary.replace(target)


def refresh_cache(cache, packages):
    refreshed = 0
    for package in packages:
        target = cache / package.id / package.version
        if not target.exists():
            continue
        if not target.resolve().is_relative_to(cache.resolve()):
            raise ValueError(f"Cache entry resolves outside the local cache: {target}")
        cached = target / f"{package.id}.{package.version}.nupkg"
        if cached.is_file() and file_hash(cached) == file_hash(package.path):
            continue
        shutil.rmtree(target)
        refreshed += 1
    return refreshed


def file_hash(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").digest()


def pull(args):
    precisions = ("single", "double") if args.precision == "both" else (args.precision,)
    branch = args.branch or command("git", "branch", "--show-current", capture=True).strip()
    if not branch and not args.run:
        raise ValueError("Detached checkout: specify --branch or --run")
    run, artifacts = choose_run(args.repo, branch, args.run, precisions)
    native_commit = api(args.repo, f"contents/godot?ref={run['head_sha']}")["sha"]
    checkout_native = command("git", "rev-parse", "HEAD:godot", capture=True).strip()
    if native_commit != checkout_native:
        raise ValueError("The selected run uses a different Godot commit. Update the checkout "
                         "or select a matching --run before importing its packages.")
    names = [f"nuget-packages-{p}" for p in precisions]
    if any(a["name"] == "nuget-packages-maui" and not a["expired"] for a in artifacts):
        names.append("nuget-packages-maui")
    print(f"CI: {run['html_url']} ({run['head_sha'][:12]})", flush=True)
    for name in names:
        artifact = next(a for a in artifacts if a["name"] == name)
        print(f"  {name}: {artifact['size_in_bytes'] / 1_000_000:.1f} MB; "
              f"expires {artifact['expires_at']}", flush=True)
    if args.plan:
        return
    expected = versions(ROOT / "Directory.Build.props")
    scratch = ROOT / "temp"
    scratch.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="ci-packages-", dir=scratch) as directory:
        staging = Path(directory)
        for name in names:
            command("gh", "run", "download", run["id"], "--repo", args.repo,
                    "--name", name, "--dir", staging / name)
        packages = collect_packages(staging, precisions, expected, run["head_sha"], native_commit)
        bindings = []
        for precision in precisions:
            assets = staging / f"bindings-{precision}"
            assets.mkdir()
            tag = f"godot-{native_commit[:7]}" + ("-double" if precision == "double" else "")
            command("gh", "release", "download", tag, "--repo", args.repo, "--dir", assets,
                    "--pattern", "godotsharp.zip", "--pattern", "godotsharp-nupkgs.zip",
                    "--pattern", "godot-source-generators.zip")
            for name, required in (
                ("godotsharp", ["Api/Debug/GodotSharp.dll", "Api/Release/GodotSharp.dll"]),
                ("godotsharp-nupkgs", [f"Godot.NET.Sdk.{expected[0]}.nupkg"]),
                ("godot-source-generators", ["Godot.SourceGenerators.dll"]),
            ):
                extract_checked(assets / f"{name}.zip", assets / name, required)
            sdk = [inspect_package(p) for p in (assets / "godotsharp-nupkgs").glob("*.nupkg")]
            if any(p.version != expected[0] for p in sdk):
                raise ValueError("Godot SDK package versions do not match this checkout")
            bindings.append((precision, assets, sdk))
        # Everything has downloaded and validated before changing the local feed or cache.
        feed = ROOT / "packages"
        for package in packages:
            copy_file(package.path, feed / package.path.name)
        for index, (precision, assets, sdk) in enumerate(bindings):
            bin_dir = ROOT / "godot/bin" / ("double" if precision == "double" else "")
            shutil.copytree(assets / "godotsharp", bin_dir / "GodotSharp", dirs_exist_ok=True)
            if index == 0:
                for package in sdk:
                    copy_file(package.path, feed / package.path.name)
                    copy_file(package.path, ROOT / "godot/bin/GodotSharp/Tools/nupkgs" / package.path.name)
                generator = ROOT / "godot/modules/mono/editor/Godot.NET.Sdk/Godot.SourceGenerators/bin/Release/netstandard2.0"
                shutil.copytree(assets / "godot-source-generators", generator, dirs_exist_ok=True)
                packages.extend(sdk)
        refreshed = refresh_cache(ROOT / ".packages", packages)
        manifest = {"run": run["html_url"], "commit": run["head_sha"], "godot": native_commit,
                    "precision": args.precision, "packages": {p.id: p.version for p in packages}}
        (feed / "ci-packages.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        print(f"Imported {len(packages)} packages into {feed}; refreshed {refreshed} stale cache entries.", flush=True)
    if not args.no_restore:
        for precision in precisions:
            command("dotnet", "restore", "2dog.tests.slnf", "--force", f"-p:TwoDogPrecision={precision}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    selection = parser.add_mutually_exclusive_group()
    selection.add_argument("--run", type=int, help="Use this successful CI run instead of the latest available one")
    selection.add_argument("--branch", help="Branch to search (default: current branch)")
    parser.add_argument("--repo", default="outfox/2dog", help="GitHub repository (default: outfox/2dog)")
    parser.add_argument("--precision", choices=("single", "double", "both"), default="single")
    parser.add_argument("--plan", action="store_true", help="Show the selected run and download sizes without importing")
    parser.add_argument("--no-restore", action="store_true", help="Import packages and bindings without restoring projects")
    try:
        pull(parser.parse_args())
    except (OSError, ValueError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print(f"Package download failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
