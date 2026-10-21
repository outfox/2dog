#!/usr/bin/env python3
"""Verify an iOS .app bundle carries a Mach-O arm64 executable for the selected slice, the exported game pack and
the game assembly, and that its Info.plist names the bundle."""
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import struct

# Mach-O 64-bit magic (little-endian on disk), CPU_TYPE_ARM64 and the platform of the LC_BUILD_VERSION command.
MACHO_MAGIC_64 = 0xFEEDFACF
CPU_TYPE_ARM64 = 0x0100000C
LC_BUILD_VERSION = 0x32
PLATFORM_IOS = 2
PLATFORM_IOS_SIMULATOR = 7
RID_PLATFORM = {"ios-arm64": PLATFORM_IOS, "iossimulator-arm64": PLATFORM_IOS_SIMULATOR}


def macho_platforms(data):
    """The LC_BUILD_VERSION platform ids of a thin 64-bit Mach-O image (an empty set when there are none)."""
    if len(data) < 32 or struct.unpack("<I", data[:4])[0] != MACHO_MAGIC_64:
        raise ValueError("not a 64-bit Mach-O image")
    cpu_type, _, _, ncmds, _ = struct.unpack("<IIIII", data[4:24])
    if cpu_type != CPU_TYPE_ARM64:
        raise ValueError(f"not an arm64 image (cpu type {cpu_type:#x})")
    platforms = set()
    offset = 32
    for _ in range(ncmds):
        if offset + 8 > len(data):
            raise ValueError("truncated load commands")
        command, size = struct.unpack("<II", data[offset:offset + 8])
        if size < 8:
            raise ValueError("corrupt load command")
        if command == LC_BUILD_VERSION:
            platforms.add(struct.unpack("<I", data[offset + 8:offset + 12])[0])
        offset += size
    return platforms


def inspect_app(app, rid, pack, game_assembly="ios-smoke-game"):
    app = Path(app)
    if rid not in RID_PLATFORM:
        raise ValueError(f"unsupported RID {rid}")
    info_path = app / "Info.plist"
    if not info_path.is_file():
        raise ValueError(f"{app} has no Info.plist")
    with info_path.open("rb") as stream:
        info = plistlib.load(stream)
    executable = info.get("CFBundleExecutable")
    bundle_id = info.get("CFBundleIdentifier")
    if not executable or not bundle_id:
        raise ValueError("Info.plist lacks CFBundleExecutable or CFBundleIdentifier")
    binary = app / executable
    if not binary.is_file():
        raise ValueError(f"app is missing its executable {executable}")
    platforms = macho_platforms(binary.read_bytes())
    if platforms and RID_PLATFORM[rid] not in platforms:
        raise ValueError(f"{executable} was linked for platform ids {sorted(platforms)}, not for {rid}")
    bundled_pack = app / "game.pck"
    if not bundled_pack.is_file():
        raise ValueError("app is missing game.pck")
    if bundled_pack.read_bytes() != Path(pack).read_bytes():
        raise ValueError("app game.pck differs from the exported pack")
    # .NET for iOS keeps managed assemblies at the bundle root (older layouts used .monotouch-64/).
    assembly = next((candidate for candidate in (app / f"{game_assembly}.dll", app / ".monotouch-64" / f"{game_assembly}.dll")
                     if candidate.is_file()), None)
    if assembly is None:
        raise ValueError(f"app is missing the game assembly {game_assembly}.dll")
    return {"app": str(app), "rid": rid, "bundle_id": bundle_id, "executable": executable,
            "platforms": sorted(platforms), "signed": (app / "_CodeSignature").is_dir(),
            "pack_sha256": hashlib.sha256(bundled_pack.read_bytes()).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    parser.add_argument("--rid", choices=RID_PLATFORM, required=True)
    parser.add_argument("--pack", type=Path, required=True)
    parser.add_argument("--game-assembly", default="ios-smoke-game")
    args = parser.parse_args()
    try:
        print(json.dumps(inspect_app(args.app, args.rid, args.pack, args.game_assembly), indent=2))
    except (ValueError, OSError) as error:
        parser.exit(1, f"iOS app validation failed: {error}\n")


if __name__ == "__main__":
    main()
