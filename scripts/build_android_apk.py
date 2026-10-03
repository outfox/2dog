#!/usr/bin/env python3
"""Build an Android APK (the smoke test or the showcase) against 2dog packages: CI's packed feed (--feed), or a
private feed packed here from staged Godot bindings and Android payloads."""
import argparse
from dataclasses import dataclass
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

from inspect_android_apk import inspect_apk


REPO = Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class App:
    game: Path
    project: str
    host: Path
    assembly: str
    game_properties: tuple = ()
    # Extra libraries the APK must carry in its ABI directory; {arch} is Godot's architecture name.
    libraries: tuple = ()


APPS = {
    "smoke": App(REPO / "tests/android/game", "android-smoke-game.csproj",
                 REPO / "tests/android/host/android-smoke.csproj", "android-smoke-game"),
    # The showcase's GDExtension probe must exist for both ABIs before the pck export lists it.
    "showcase": App(REPO / "demos/showcase", "showcase.csproj",
                    REPO / "demos/showcase/showcase.android/showcase.android.csproj", "showcase",
                    ("-p:TwoDogProbeAndroid=true",), ("libtwodog_probe.android.{arch}.so",)),
}


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
    if args.skip_java and args.feed is None:
        validate_java_payloads()
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


def pack_local_feed(args, arch, feed, properties):
    if not args.skip_native:
        command = [sys.executable, REPO / "build-godot.py", "--platform", "android", "--arch", arch,
                   "--debug-symbols", "no", "--dev-build", "no"]
        if args.cache_path:
            command += ["--cache-path", args.cache_path.resolve()]
        run(command)
    if not args.skip_java:
        run([sys.executable, REPO / "scripts/build_android_java.py"])
    validate_java_payloads()
    for target in ("template_debug", "template_release"):
        for library in ("libgodot_android.so", "libc++_shared.so"):
            if not (REPO / f"godot/bin/android/{target}/{arch}/{library}").is_file():
                raise ValueError(f"Missing {target}/{arch}/{library}; build both native variants first")
    # Local-only desktop stubs satisfy 2dog.engine's desktop dependencies.
    # This feed is an APK build artifact and must never be published.
    pack = [*properties, f"-p:PackageOutputPath={feed}"]
    run([args.dotnet, "pack", REPO / "platforms", "-c", "Release", "-p:ForcePackAllPlatforms=true", *pack])
    for project in ("twodog.godotsharp", "twodog.godotsharp.editor", "twodog.engine"):
        run([args.dotnet, "pack", REPO / project, "-c", "Release", *pack])


def build_with_packages(args, output, feed, config, restore):
    app = APPS[args.app]
    arch = "x86_64" if args.rid == "android-x64" else "arm64"
    properties = [f"-p:RestoreConfigFile={config}", f"-p:RestorePackagesPath={restore}", "-p:NuGetAudit=false"]
    editor = args.editor.resolve()
    if args.feed is None:
        pack_local_feed(args, arch, feed, properties)
    # The selected variant's natives, which the APK must carry byte for byte.
    native = REPO / f"godot/bin/android/template_{args.configuration.lower()}/{arch}"
    if not (native / "libgodot_android.so").is_file():
        raise ValueError(f"Missing {native / 'libgodot_android.so'}; stage the Android natives first")
    run([args.dotnet, "build", app.game / app.project, "-c", args.configuration, *app.game_properties, *properties])
    run([editor, "--headless", "--editor", "--path", app.game, "--import"])
    pack = output / "game.pck"
    run([editor, "--headless", "--path", app.game, "--export-pack", "Android", pack])
    apk_output = output / "apk"
    publish = [args.dotnet, "publish", app.host, "-c", args.configuration, "-r", args.rid,
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
    libraries = [library.format(arch=arch) for library in app.libraries]
    report = inspect_apk(apks[0], args.rid, native, pack, app.assembly, libraries)
    report["app"] = args.app
    report["configuration"] = args.configuration
    (output / "apk-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Verified APK: {apks[0]}", flush=True)
    return apks[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", choices=APPS, default="smoke")
    parser.add_argument("--rid", choices=("android-x64", "android-arm64"), default="android-x64")
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    parser.add_argument("--editor", required=True, type=Path, help="Source-matched Godot Mono editor executable")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", type=Path,
                        help="Default: artifacts/android-apk (smoke) or artifacts/android-<app>-apk")
    parser.add_argument("--feed", type=Path, help="Build against these packed 2dog packages (CI's pack job "
                        "output) instead of building natives and Java payloads and packing them here")
    parser.add_argument("--cache-path", type=Path)
    parser.add_argument("--skip-native", action="store_true", help="Reuse both staged native variants")
    parser.add_argument("--skip-java", action="store_true", help="Reuse both staged Java AAR variants")
    args = parser.parse_args()
    if args.output is None:
        args.output = REPO / ("artifacts/android-apk" if args.app == "smoke" else f"artifacts/android-{args.app}-apk")
    if not args.editor.is_file():
        parser.error("--editor must point to a built Godot Mono editor")
    try:
        build(args)
    except (ValueError, subprocess.CalledProcessError, OSError) as error:
        parser.exit(1, f"Android APK build failed: {error}\n")


if __name__ == "__main__":
    main()
