#!/usr/bin/env python3
"""Verify an APK contains the selected Godot native payload and exported game assets."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


RID_ABI = {"android-x64": ("x86_64", 62), "android-arm64": ("arm64-v8a", 183)}


def inspect_apk(apk, rid, native_directory, pack, game_assembly="android-smoke-game", libraries=()):
    abi, machine = RID_ABI[rid]
    with zipfile.ZipFile(apk) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("APK contains duplicate ZIP entries")
        for required in ("AndroidManifest.xml", "classes.dex", "assets/game.pck",
                         f"assets/2dog/{game_assembly}.dll", *(f"lib/{abi}/{library}" for library in libraries)):
            if required not in names:
                raise ValueError(f"APK is missing {required}")
        actual_abis = {name.split("/")[1] for name in names if name.startswith("lib/") and name.endswith(".so")}
        if actual_abis != {abi}:
            raise ValueError(f"Expected only ABI {abi}, found {sorted(actual_abis)}")
        hashes = {}
        for library in ("libgodot_android.so", "libc++_shared.so"):
            name = f"lib/{abi}/{library}"
            if name not in names:
                raise ValueError(f"APK is missing {name}")
            data = archive.read(name)
            if len(data) < 20 or data[:6] != b"\x7fELF\x02\x01" or int.from_bytes(data[18:20], "little") != machine:
                raise ValueError(f"{name} is not a 64-bit Android {abi} ELF library")
            digest = hashlib.sha256(data).hexdigest()
            if digest != hashlib.sha256((native_directory / library).read_bytes()).hexdigest():
                raise ValueError(f"APK {library} differs from the selected native variant")
            hashes[library] = digest
        if archive.read("assets/game.pck") != pack.read_bytes():
            raise ValueError("APK game.pck differs from the exported pack")
    return {"apk": str(apk), "rid": rid, "abi": abi, "sha256": hashlib.sha256(apk.read_bytes()).hexdigest(),
            "native_sha256": hashes}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--rid", choices=RID_ABI, required=True)
    parser.add_argument("--native-directory", type=Path, required=True)
    parser.add_argument("--pack", type=Path, required=True)
    parser.add_argument("--game-assembly", default="android-smoke-game")
    parser.add_argument("--library", action="append", default=[], help="Extra library required in the ABI directory")
    args = parser.parse_args()
    try:
        print(json.dumps(inspect_apk(args.apk, args.rid, args.native_directory, args.pack, args.game_assembly,
                                     args.library), indent=2))
    except (ValueError, OSError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Android APK validation failed: {error}\n")


if __name__ == "__main__":
    main()
