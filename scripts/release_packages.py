"""Validate complete release feeds and bind immutable package bytes to their CI run."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def inspect(path):
    with zipfile.ZipFile(path) as archive:
        nuspec, = (name for name in archive.namelist() if name.endswith(".nuspec"))
        metadata = ET.fromstring(archive.read(nuspec)).find("{*}metadata")
        return metadata.findtext("{*}id").lower(), metadata.findtext("{*}version")


def expected_packages():
    managed = {"2dog", *(f"2dog.{name}" for name in (
        "templates", "godotsharp", "godotsharp.editor", "engine", "avalonia", "blazor", "qt", "repl", "gdunit", "xunit", "nunit", "maui", "ios"))}
    native = {"2dog.android"}
    for precision in ("", ".double"):
        managed.update(f"2dog.{name}{precision}" for name in ("engine", "godotsharp", "godotsharp.editor", "gdunit"))
        native.add(f"2dog.tools{precision}")
        for rid, variants in (("win-x64", ("debug", "release", "editor")), ("linux-x64", ("debug", "release", "editor")),
                              ("osx-arm64", ("debug", "release", "editor")), ("browser-wasm", ("debug", "release")),
                              ("android-arm64", ("debug", "release")), ("android-x64", ("debug", "release")),
                              ("ios-arm64", ("debug", "release")), ("iossimulator-arm64", ("debug", "release"))):
            native.update(f"2dog.{rid}{variant}{precision}" for variant in ("", *(f".{v}" for v in variants)))
    return managed, native


def validate(feed, tag, manifest=None):
    if not re.fullmatch(r"v\d+(?:\.\d+){3}(?:-[0-9A-Za-z.-]+)?", tag):
        raise ValueError("Expected a release version tag")
    packages = {}
    for path in sorted(feed.glob("*.nupkg")):
        name, version = inspect(path)
        if name in packages:
            raise ValueError(f"Duplicate package ID: {name}")
        if path.name.lower() != f"{name}.{version}.nupkg".lower():
            raise ValueError(f"Package filename disagrees with its ID/version: {path.name}")
        packages[name] = (path, version)
    managed, native = expected_packages()
    if missing := (managed | native) - packages.keys():
        raise ValueError(f"Incomplete release: {', '.join(sorted(missing))}")
    if unexpected := packages.keys() - (managed | native):
        raise ValueError(f"Unexpected release packages: {', '.join(sorted(unexpected))}")
    for name in managed:
        if packages[name][1] != tag[1:]:
            raise ValueError(f"{name}: managed version does not match {tag}")
    native_versions = {packages[name][1] for name in native}
    if len(native_versions) != 1 or not re.fullmatch(r"\d+(?:\.\d+){2,3}", next(iter(native_versions))):
        raise ValueError("Native packages must share one stable NativesVersion")
    # Inspect exact local dependencies as well as the expected top-level package set.
    for path, _ in packages.values():
        with zipfile.ZipFile(path) as archive:
            nuspec, = (name for name in archive.namelist() if name.endswith(".nuspec"))
            for dependency in ET.fromstring(archive.read(nuspec)).findall(".//{*}dependency"):
                name = dependency.get("id", "").lower()
                if name == "2dog" or name.startswith("2dog."):
                    wanted = dependency.get("version", "").strip("[]")
                    if name not in packages or packages[name][1] != wanted:
                        raise ValueError(f"{path.name}: missing or mismatched dependency {name} {wanted}")
    for precision in ("single", "double"):
        path = feed / f"native-build-{precision}.json"
        if not path.is_file():
            raise ValueError(f"Missing native build metadata: {path.name}")
        native_build = json.loads(path.read_text())
        if (native_build["precision"] != precision or not re.fullmatch(r"[0-9a-f]{64}", native_build["identity"])
                or native_build["tag"] != f"godot-{native_build['identity']}" + ("-double" if precision == "double" else "")):
            raise ValueError(f"Invalid native build metadata: {path.name}")
    files = {path.name: sha256(path) for path in sorted(feed.iterdir())
             if path.suffix in (".nupkg", ".snupkg") or path.name.startswith("native-build-") and path.suffix == ".json"}
    if manifest is not None and (manifest["tag"] != tag or manifest["files"] != files):
        raise ValueError("Release assets differ from the staged manifest")
    return files


# Staging runs ahead of the tests, so promotion requires the staged run to have finished green. A skipped job does not
# fail a run, so the jobs that carry the verdicts must also have run and passed in their latest attempt.
PROMOTION_GATES = ("Release Gate", "iOS Release Smoke")


def require_passed_run(manifest, repo):
    run = int(manifest["run_id"])
    source = json.loads(subprocess.check_output(["gh", "api", f"repos/{repo}/actions/runs/{run}"], text=True))
    if source["head_sha"] != manifest["commit"] or source["path"] != ".github/workflows/ci.yml":
        raise ValueError("Release manifest does not identify the source CI run")
    if (source["status"] != "completed" or source["conclusion"] != "success"
            or int(source["run_attempt"]) < int(manifest["run_attempt"])):
        raise ValueError(f"The staged CI run {run} has not completed successfully "
                         f"({source['status']}, {source['conclusion']}); promotion is blocked")
    pages = json.loads(subprocess.check_output(["gh", "api", "--paginate", "--slurp",
                    f"repos/{repo}/actions/runs/{run}/jobs?filter=all&per_page=100"], text=True))
    for gate in PROMOTION_GATES:
        jobs = [job for page in pages for job in page["jobs"]
                if job["name"] == gate or job["name"].startswith(f"{gate} / ")]
        attempt = max((int(job["run_attempt"]) for job in jobs), default=0)
        if (attempt < int(manifest["run_attempt"]) or not jobs
                or any(job["conclusion"] != "success" for job in jobs if int(job["run_attempt"]) == attempt)):
            raise ValueError(f"The staged run's {gate} has not succeeded; promotion is blocked")


def ios_inputs(feed, tag, repo):
    # Test the immutable staged bytes, including when a failed job is retried after another pack attempt.
    patterns = ("2dog.engine*.nupkg", "2dog.godotsharp*.nupkg", "2dog.tools*.nupkg", "2dog.win-x64*.nupkg",
                "2dog.linux-x64*.nupkg", "2dog.osx-arm64*.nupkg", "2dog.ios*.nupkg", "2dog.maui*.nupkg",
                "release-manifest.json", "native-build-double.json")
    subprocess.run(["gh", "release", "download", tag, "--repo", repo, "--dir", str(feed),
                    *(arg for pattern in patterns for arg in ("--pattern", pattern))], check=True)
    manifest = json.loads((feed / "release-manifest.json").read_text())
    if manifest["tag"] != tag:
        raise ValueError("iOS inputs do not belong to the staged release")
    for path in feed.iterdir():
        if path.name == ".gitkeep":
            continue
        if path.name not in manifest["files"] or manifest["files"][path.name] != sha256(path):
            if path.name != "release-manifest.json":
                raise ValueError(f"Staged iOS input checksum mismatch: {path.name}")
    native = json.loads((feed / "native-build-double.json").read_text())
    if not re.fullmatch(r"godot-[0-9a-f]{64}-double", native["tag"]):
        raise ValueError("Invalid native build identity")
    sdk = feed / "sdk"
    sdk.mkdir()
    subprocess.run(["gh", "release", "download", native["tag"], "--repo", repo, "--dir", str(sdk),
                    "--pattern", "godotsharp-nupkgs.zip"], check=True)
    archive = sdk / "godotsharp-nupkgs.zip"
    if sha256(archive) != native["assets"][archive.name]:
        raise ValueError("Fork SDK checksum mismatch")
    from scripts.pull_ci_packages import extract_checked
    extract_checked(archive, Path("godot/bin/GodotSharp/Tools/nupkgs"), ["Godot.NET.Sdk.*.nupkg"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("stage", "verify", "ios-inputs"))
    parser.add_argument("tag")
    parser.add_argument("--feed", type=Path, default=Path("packages"))
    parser.add_argument("--production", action="store_true")
    args = parser.parse_args()
    if args.command == "ios-inputs":
        ios_inputs(args.feed, args.tag, os.environ["GITHUB_REPOSITORY"])
        raise SystemExit(0)
    path = args.feed / "release-manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8")) if args.command == "verify" else None
    files = validate(args.feed, args.tag, manifest)
    if args.command == "stage":
        manifest = dict(tag=args.tag, files=files, commit=os.environ["GITHUB_SHA"],
                        run_id=os.environ["GITHUB_RUN_ID"], run_attempt=os.environ["GITHUB_RUN_ATTEMPT"])
        path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    if args.production:
        require_passed_run(manifest, os.environ["GITHUB_REPOSITORY"])
    print(f"Validated {len(files)} release assets for {args.tag}")
