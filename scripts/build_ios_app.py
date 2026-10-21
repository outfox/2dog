#!/usr/bin/env python3
"""Build an iOS host (the smoke test or the showcase) the way an app does - `dotnet build` for the Simulator or
`dotnet publish` for a device, which exports the game pck itself - against 2dog packages: CI's packed feed (--feed),
or a private feed packed here from staged Godot bindings and iOS archives. Then verify the .app bundle. macOS with
Xcode only; the private feed is a build artifact and must never be published."""
import argparse
from dataclasses import dataclass
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

from inspect_ios_app import inspect_app


REPO = Path(__file__).resolve().parents[1]
RIDS = ("iossimulator-arm64", "ios-arm64")


@dataclass(frozen=True)
class App:
    host: Path
    assembly: str


APPS = {
    "smoke": App(REPO / "tests/ios/host/ios-smoke.csproj", "ios-smoke-game"),
    "showcase": App(REPO / "demos/showcase/showcase.ios/showcase.ios.csproj", "showcase"),
    "maui": App(REPO / "demos/showcase/showcase.maui/showcase.maui.csproj", "showcase"),
}


def run(command):
    print("+ " + subprocess.list2cmdline(list(map(str, command))), flush=True)
    subprocess.run(list(map(str, command)), cwd=REPO, check=True)


def restore_config(path, feed):
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    for name, value in (("ios-packages", feed), ("godot-sdk", REPO / "godot/bin/GodotSharp/Tools/nupkgs"),
                        ("nuget.org", "https://api.nuget.org/v3/index.json")):
        ET.SubElement(sources, "add", key=name, value=str(value))
    ET.ElementTree(config).write(path, encoding="utf-8", xml_declaration=True)


def native_archive(precision, configuration, rid):
    subdir = "double/" if precision == "double" else ""
    return REPO / f"godot/bin/{subdir}ios/template_{configuration.lower()}/{rid}/libgodot.a"


def build(args):
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if args.feed is None:
        feed = output / "packages"
        feed.mkdir(exist_ok=True)
    else:
        feed = args.feed.resolve()
        if not feed.is_dir():
            raise ValueError(f"Package feed {feed} does not exist")
    config = output / "NuGet.Config"
    restore_config(config, feed)
    # SDK resolution precedes project restore settings and reads repository sources.
    (REPO / "packages").mkdir(exist_ok=True)
    (REPO / "godot/bin/GodotSharp/Tools/nupkgs").mkdir(parents=True, exist_ok=True)
    # Rebuilt local packages must not reuse an older payload at the same version.
    with tempfile.TemporaryDirectory(prefix="restore-", dir=output) as restore:
        return build_with_packages(args, output, feed, config, Path(restore))


def pack_local_feed(args, feed, properties):
    if not args.skip_native:
        for simulator in (False, True):
            command = [sys.executable, REPO / "build-godot.py", "--platform", "ios",
                       "--debug-symbols", "no", "--dev-build", "no", "--precision", args.precision]
            if simulator:
                command.append("--simulator")
            if args.cache_path:
                command += ["--cache-path", args.cache_path.resolve()]
            run(command)
    for configuration in ("Debug", "Release"):
        for rid in RIDS:
            if not native_archive(args.precision, configuration, rid).is_file():
                raise ValueError(f"Missing {native_archive(args.precision, configuration, rid)}; build both iOS slices first")
    pack = [*properties, f"-p:PackageOutputPath={feed}"]
    # Local-only desktop stubs satisfy 2dog.engine's desktop dependencies.
    run([args.dotnet, "pack", REPO / "platforms", "-c", "Release", "-p:ForcePackAllPlatforms=true", *pack])
    for project in ("twodog.godotsharp", "twodog.godotsharp.editor", "twodog.engine", "twodog.ios"):
        run([args.dotnet, "pack", REPO / project, "-c", "Release", *pack])


def build_with_packages(args, output, feed, config, restore):
    app = APPS[args.app]
    properties = [f"-p:RestoreConfigFile={config}", f"-p:RestorePackagesPath={restore}", "-p:NuGetAudit=false",
                  f"-p:TwoDogPrecision={args.precision}"]
    if args.feed is None:
        pack_local_feed(args, feed, properties)
    # The build exports the pck; directing it here lets the inspection compare it with the bundle's copy.
    pack = output / "game.pck"
    app_output = output / "app"
    device = args.rid == "ios-arm64"
    # Simulator bundles come out of a plain build; devices need a signed publish.
    verb = "publish" if device else "build"
    command = [args.dotnet, verb, app.host, "-c", args.configuration, "-r", args.rid, "-o", app_output,
               f"-p:TwoDogIosExportPath={pack}", *properties]
    if args.app == "maui":
        command += ["-f", "net10.0-ios", "-p:TwoDogMauiAndroid=false"]
    if device:
        if not args.codesign_key:
            raise ValueError("Device builds need --codesign-key (and usually --codesign-provision)")
        command.append(f"-p:CodesignKey={args.codesign_key}")
        if args.codesign_provision:
            command.append(f"-p:CodesignProvision={args.codesign_provision}")
    if args.editor:
        command.append(f"-p:GodotEditor={args.editor.resolve()}")
    run(command)
    apps = list(app_output.glob("*.app"))
    if len(apps) != 1:
        raise ValueError(f"Expected one .app bundle in {app_output}, found {len(apps)}")
    report = inspect_app(apps[0], args.rid, pack, app.assembly)
    report["app_name"] = args.app
    report["configuration"] = args.configuration
    (output / "app-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Verified app bundle: {apps[0]}", flush=True)
    return apps[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", choices=APPS, default="smoke")
    parser.add_argument("--rid", choices=RIDS, default="iossimulator-arm64")
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    parser.add_argument("--precision", choices=("single", "double"), default=os.environ.get("TwoDogPrecision", "single"))
    parser.add_argument("--editor", type=Path, help="Source-matched Godot Mono editor executable for the pck "
                        "export (default: the editor libgodot from the 2dog.osx-arm64.editor package)")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", type=Path, help="Default: artifacts/ios-app (smoke) or artifacts/ios-<app>-app")
    parser.add_argument("--feed", type=Path, help="Build against these packed 2dog packages (CI's pack job output) "
                        "instead of building the archives and packing them here")
    parser.add_argument("--cache-path", type=Path)
    parser.add_argument("--skip-native", action="store_true", help="Reuse the staged device and Simulator archives")
    parser.add_argument("--codesign-key", help="Device builds: the signing identity (CodesignKey)")
    parser.add_argument("--codesign-provision", help="Device builds: the provisioning profile (CodesignProvision)")
    args = parser.parse_args()
    if args.output is None:
        args.output = REPO / ("artifacts/ios-app" if args.app == "smoke" else f"artifacts/ios-{args.app}-app")
    if args.editor and not args.editor.is_file():
        parser.error("--editor must point to a built Godot Mono editor")
    try:
        build(args)
    except (ValueError, subprocess.CalledProcessError, OSError) as error:
        parser.exit(1, f"iOS app build failed: {error}\n")


if __name__ == "__main__":
    main()
