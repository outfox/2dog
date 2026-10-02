#!/usr/bin/env python3
"""Publish an experimental .NET Android host to an APK or AAB using an already exported PCK."""
import argparse
from pathlib import Path
import subprocess


def publish_command(args):
    return ["dotnet", "publish", str(args.project.resolve()), "-c", args.configuration,
            "-r", args.rid, "-o", str(args.output.resolve()),
            f"-p:TwoDogVariant={args.variant or args.configuration.lower()}",
            f"-p:TwoDogAndroidPack={args.pack.resolve()}",
            f"-p:AndroidPackageFormats={args.format}",
            "-p:PublishTrimmed=false", "-p:RunAOTCompilation=false", "-p:PublishAot=false",
            *[f"-p:{value}" for value in args.property]]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=Path)
    parser.add_argument("--pack", required=True, type=Path, help="Godot PCK exported using an Android preset")
    parser.add_argument("--rid", choices=("android-arm64", "android-x64"), default="android-arm64")
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Release")
    parser.add_argument("--variant", choices=("debug", "release"))
    parser.add_argument("--format", choices=("apk", "aab"), default="apk")
    parser.add_argument("--output", type=Path, default=Path("artifacts/android"))
    parser.add_argument("--property", action="append", default=[], help="Additional MSBuild NAME=VALUE (repeatable)")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    if not args.project.exists():
        parser.error(f"Project does not exist: {args.project}")
    if not args.pack.is_file():
        parser.error(f"Exported PCK does not exist: {args.pack}")
    for value in args.property:
        if "=" not in value:
            parser.error("--property requires NAME=VALUE")
        if value.split("=", 1)[0].lower() in {"twodogvariant", "twodogandroidpack", "runtimeidentifier",
                                             "androidpackageformats", "publishtrimmed", "runaotcompilation", "publishaot"}:
            parser.error(f"Use the dedicated option for {value.split('=', 1)[0]}")
    command = publish_command(args)
    if args.dry_run:
        # Property values may contain signing secrets; do not print them.
        print(subprocess.list2cmdline(command[:-len(args.property)] if args.property else command))
        if args.property:
            print(f"({len(args.property)} additional MSBuild properties hidden)")
        return
    subprocess.run(command, check=True)


if __name__ == "__main__":
    main()
