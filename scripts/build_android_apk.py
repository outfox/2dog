#!/usr/bin/env python3
"""Build the Android smoke APK from staged Godot bindings and freshly packed local NuGets."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

from inspect_android_apk import inspect_apk


REPO = Path(__file__).resolve().parents[1]
GAME = REPO / "tests/android/game"
HOST = REPO / "tests/android/host/android-smoke.csproj"


def run(command):
    print("+ " + subprocess.list2cmdline(list(map(str, command))), flush=True)
    subprocess.run(list(map(str, command)), cwd=REPO, check=True)


def restore_config(path, feed):
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    for name, value in (("apk-packages", feed), ("godot-sdk", REPO / "godot/bin/GodotSharp/Tools/nupkgs"),
                        ("nuget.org", "https://api.nuget.org/v3/index.json")):
        ET.SubElement(sources, "add", key=name, value=str(value))
    ET.ElementTree(config).write(path, encoding="utf-8", xml_declaration=True)


def build(args):
    if args.skip_java:
        validate_java_payloads()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    feed = output / "packages"
    feed.mkdir(exist_ok=True)
    config = output / "NuGet.Config"
    restore_config(config, feed)
    # SDK resolution precedes project restore settings and reads repository sources.
    # Keep those existing local source folders valid; restores use the private config.
    (REPO / "packages").mkdir(exist_ok=True)
    (REPO / "godot/bin/GodotSharp/Tools/nupkgs").mkdir(parents=True, exist_ok=True)
    # Rebuilt local packages must not reuse an older payload at the same version.
    # Keep the fresh cache alive through inspection, then clean it on every exit.
    with tempfile.TemporaryDirectory(prefix="restore-", dir=output) as restore:
        return build_with_packages(args, output, feed, config, Path(restore))


def validate_java_payloads():
    for variant in ("debug", "release"):
        if not (REPO / f"godot/bin/android/java/{variant}/godot.aar").is_file():
            raise ValueError(f"Missing {variant} Android Java payload; build both Java variants first")


def build_with_packages(args, output, feed, config, restore):
    properties = [f"-p:RestoreConfigFile={config}", f"-p:RestorePackagesPath={restore}",
                  f"-p:PackageOutputPath={feed}", "-p:NuGetAudit=false"]
    editor = args.editor.resolve()
    run([editor, "--headless", "--generate-mono-glue", REPO / "godot/modules/mono/glue"])
    if not args.skip_native:
        arch = "x86_64" if args.rid == "android-x64" else "arm64"
        command = [sys.executable, REPO / "build-godot.py", "--platform", "android", "--arch", arch,
                   "--debug-symbols", "no", "--dev-build", "no"]
        if args.cache_path:
            command += ["--cache-path", args.cache_path.resolve()]
        run(command)
    if not args.skip_java:
        run([sys.executable, REPO / "scripts/build_android_java.py"])
    validate_java_payloads()
    for target in ("template_debug", "template_release"):
        arch = "x86_64" if args.rid == "android-x64" else "arm64"
        for library in ("libgodot_android.so", "libc++_shared.so"):
            if not (REPO / f"godot/bin/android/{target}/{arch}/{library}").is_file():
                raise ValueError(f"Missing {target}/{arch}/{library}; build both native variants first")
    # Local-only desktop stubs satisfy 2dog.engine's desktop dependencies.
    # This feed is an APK build artifact and must never be published.
    run([args.dotnet, "pack", REPO / "platforms", "-c", "Release", "-p:ForcePackAllPlatforms=true", *properties])
    for project in ("twodog.godotsharp", "twodog.godotsharp.editor", "twodog.engine"):
        run([args.dotnet, "pack", REPO / project, "-c", "Release", *properties])
    run([args.dotnet, "build", GAME / "android-smoke-game.csproj", "-c", args.configuration, *properties])
    run([editor, "--headless", "--editor", "--path", GAME, "--import"])
    pack = output / "game.pck"
    run([editor, "--headless", "--path", GAME, "--export-pack", "Android", pack])
    apk_output = output / "apk"
    publish = [args.dotnet, "publish", HOST, "-c", args.configuration, "-r", args.rid,
               "-o", apk_output, f"-p:TwoDogAndroidPack={pack}", "-p:AndroidPackageFormats=apk",
               "-p:PublishTrimmed=false", "-p:RunAOTCompilation=false", *properties]
    if os.environ.get("ANDROID_HOME"):
        publish.append(f"-p:AndroidSdkDirectory={os.environ['ANDROID_HOME']}")
    if os.environ.get("JAVA_HOME"):
        publish.append(f"-p:JavaSdkDirectory={os.environ['JAVA_HOME']}")
    run(publish)
    apks = list(apk_output.glob("*-Signed.apk"))
    if len(apks) != 1:
        raise ValueError(f"Expected one signed APK in {apk_output}, found {len(apks)}")
    variant = args.configuration.lower()
    arch = "x86_64" if args.rid == "android-x64" else "arm64"
    native = REPO / f"godot/bin/android/template_{variant}/{arch}"
    report = inspect_apk(apks[0], args.rid, native, pack)
    report["configuration"] = args.configuration
    (output / "apk-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Verified APK: {apks[0]}", flush=True)
    return apks[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", choices=("android-x64", "android-arm64"), default="android-x64")
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    parser.add_argument("--editor", required=True, type=Path, help="Source-matched Godot Mono editor executable")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", type=Path, default=REPO / "artifacts/android-apk")
    parser.add_argument("--cache-path", type=Path)
    parser.add_argument("--skip-native", action="store_true", help="Reuse both staged native variants")
    parser.add_argument("--skip-java", action="store_true", help="Reuse both staged Java AAR variants")
    args = parser.parse_args()
    if not args.editor.is_file():
        parser.error("--editor must point to a built Godot Mono editor")
    try:
        build(args)
    except (ValueError, subprocess.CalledProcessError, OSError) as error:
        parser.exit(1, f"Android APK build failed: {error}\n")


if __name__ == "__main__":
    main()
