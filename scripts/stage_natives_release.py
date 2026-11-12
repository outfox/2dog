"""Stage one precision's complete native release for CI packing."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import zipfile


def stage(assets, root, precision, identity=None):
    if precision not in ("single", "double"):
        raise ValueError("precision must be single or double")
    suffix = ".double" if precision == "double" else ""
    bin_dir = root / "godot/bin"
    selected = bin_dir / "double" if suffix else bin_dir
    generator = root / "godot/modules/mono/editor/Godot.NET.Sdk/Godot.SourceGenerators/bin/Release/netstandard2.0"
    editor = f"godot.linuxbsd.editor{suffix}.x86_64.executable.mono"
    manifest = [
        ("godotsharp", selected / "GodotSharp", ["Api/Release/GodotSharp.dll", "Api/Debug/GodotPlugins.dll", "Tools/GodotTools.dll"]),
        ("godotsharp-nupkgs", bin_dir / "GodotSharp/Tools/nupkgs", ["Godot.NET.Sdk.*.nupkg"]),
        ("godot-source-generators", generator, ["Godot.SourceGenerators.dll"]),
        ("godot-editor-linux-x64", bin_dir, [editor]),
        ("godot-android-java", bin_dir / "android/java", ["debug/godot.aar", "release/godot.aar"]),
    ]
    for rid, platform, arch, prefix, extension in (
        ("linux-x64", "linuxbsd", "x86_64", "libgodot", "so"),
        ("win-x64", "windows", "x86_64", "godot", "dll"),
        ("osx-arm64", "macos", "arm64", "libgodot", "dylib"),
    ):
        for target in ("template_release", "template_debug", "editor"):
            name = f"{prefix}.{platform}.{target}{suffix}.{arch}.shared_library.{extension}"
            manifest.append((f"libgodot-{rid}-{target}", selected, [name]))
    for target in ("template_release", "template_debug"):
        manifest.append((f"libgodot-browser-wasm-{target}", selected / f"web/{target}", ["libgodot/libgodot.a"]))
        manifest.append((f"libgodot-browser-wasm-threads-{target}", selected / f"web-threads/{target}",
                         ["libgodot/libgodot.a"]))
        for rid, arch in (("android-arm64", "arm64"), ("android-x64", "x86_64")):
            manifest.append((f"libgodot-{rid}-{target}", selected / f"android/{target}/{arch}",
                             ["libgodot_android.so", "libc++_shared.so"]))
        for rid in ("ios-arm64", "iossimulator-arm64"):
            manifest.append((f"libgodot-{rid}-{target}", selected / f"ios/{target}/{rid}", ["libgodot.a"]))
    # Validate every archive before extracting anything. Never turn missing platforms into publishable stubs.
    metadata_path = assets / "native-build.json"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8")) if metadata_path.exists() else None
    if identity and (not metadata or metadata["identity"] != identity or metadata["precision"] != precision
                     or metadata["tag"] != f"godot-{identity}" + ("-double" if precision == "double" else "")):
        raise ValueError("Native release identity or precision does not match this build")
    for name, destination, expected in manifest:
        if metadata:
            with (assets / f"{name}.zip").open("rb") as stream:
                actual = hashlib.file_digest(stream, "sha256").hexdigest()
            if metadata["assets"].get(f"{name}.zip") != actual:
                raise ValueError(f"Native release checksum mismatch: {name}.zip")
        with zipfile.ZipFile(assets / f"{name}.zip") as archive:
            members = [p.removeprefix("./") for p in archive.namelist()]
            for pattern in expected:
                if not any(Path(p).match(pattern) for p in members):
                    raise ValueError(f"{name}.zip lacks {pattern} for {precision} precision")
            for member in archive.infolist():
                target = (destination / member.filename).resolve()
                if not target.is_relative_to(destination.resolve()):
                    raise ValueError(f"Unsafe archive path: {member.filename}")
    for name, destination, _ in manifest:
        destination.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(assets / f"{name}.zip") as archive:
            archive.extractall(destination)
    (bin_dir / editor).chmod(0o755)
    if metadata:
        (root / "packages").mkdir(exist_ok=True)
        (root / "packages" / f"native-build-{precision}.json").write_text(json.dumps(metadata) + "\n", encoding="utf-8")
        if os.environ.get("GITHUB_ACTIONS") == "true":
            build = {name: os.environ[name] for name in ("GITHUB_REF", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_SHA")}
            build["precision"] = precision
            (root / "packages/ci-build.json").write_text(json.dumps(build) + "\n", encoding="utf-8")
    print(f"Staged complete {precision}-precision release ({len(manifest)} archives)")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("assets", type=Path)
    parser.add_argument("--precision", choices=("single", "double"), default="single")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--identity", help="Require a matching immutable native release and verify its checksums")
    args = parser.parse_args()
    stage(args.assets, args.root, args.precision, args.identity)
