"""Pinned native Dawn SDK preparation and 2dog payload staging."""

import hashlib
import json
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DAWN_TAG = "v20261014.133826"
DAWN_COMMIT = "f9ac6c61ff0a88ddec67db7f2d0455b4125ab69d"
DAWN_ARCHIVES = {
    ("windows", "x86_64"): ("windows-latest", "723182b98eb16017ac11ef0e560ca56ad2f4d31ef5162b42a2ce3ccde086ae91"),
    ("linuxbsd", "x86_64"): ("ubuntu-latest", "8685f909d0cbbf4256e3907ba72c921dbcda8687b282194740e73af348edb511"),
    ("macos", "arm64"): ("macos-latest", "c3604c8d024c29a6a2e3b69ec85bca828b8cd757992abae031bdcc2885dbe6a5"),
    ("macos", "x86_64"): ("macos-15-intel", "825d6651371502dd3bd6167e40463c9354f412fde84fc16c86236ff8668c65c8"),
}
DXC_TAG = "v1.9.2609"
DXC_NAME = "dxc_2026_09_29.zip"
DXC_SHA256 = "ad31b1fc8443175d204f77a611fdb3ef2ec42759bdc2f1167368de24a4a7e7f1"
RUNTIME = {
    "windows": "bin/godot_webgpu_dawn.dll",
    "linuxbsd": "lib/libgodot_webgpu_dawn.so",
    "macos": "lib/libgodot_webgpu_dawn.dylib",
}
RID = {"windows": "win", "linuxbsd": "linux", "macos": "osx"}


def enabled(args, platform):
    supported = (
        platform in ("windows", "linuxbsd", "macos", "web")
        and getattr(args, "precision", "single") == "single"
        and not getattr(args, "threads", False)
    )
    value = getattr(args, "webgpu", "no")
    if value == "yes" and not supported:
        raise ValueError("WebGPU currently supports single-precision desktop and single-threaded web builds only")
    return supported and value != "no"


def download(url, destination, sha256):
    destination.parent.mkdir(parents=True, exist_ok=True)
    valid = False
    if destination.is_file():
        with destination.open("rb") as stream:
            valid = hashlib.file_digest(stream, "sha256").hexdigest() == sha256
    if not valid:
        partial = destination.with_suffix(destination.suffix + ".partial")
        urllib.request.urlretrieve(url, partial)
        with partial.open("rb") as stream:
            actual = hashlib.file_digest(stream, "sha256").hexdigest()
        if actual != sha256:
            raise ValueError(f"Checksum mismatch for {url}: {actual}")
        partial.replace(destination)


def prepare_native(platform, arch):
    """Build a small shared C API boundary around the official static Dawn SDK."""
    label, digest = DAWN_ARCHIVES[(platform, arch)]
    cache = ROOT / "temp/webgpu-sdk" / f"{platform}-{arch}"
    sdk = cache / "sdk"
    stamp = sdk / "2dog-dawn.json"
    boundary = ROOT / "godot/drivers/webgpu/native"
    fingerprint = hashlib.sha256(
        b"".join((boundary / name).read_bytes() for name in ("CMakeLists.txt", "dawn_boundary.cpp", "dawn_boundary.h"))
    ).hexdigest()
    expected = {"dawn": DAWN_COMMIT, "boundary": fingerprint, "dxc": DXC_TAG if platform == "windows" else None}
    if stamp.is_file() and json.loads(stamp.read_text()) == expected and (sdk / RUNTIME[platform]).is_file():
        for source in (ROOT / "nuget/webgpu").iterdir():
            shutil.copy2(source, sdk / "licenses" / source.name)
        return sdk
    name = f"Dawn-{DAWN_COMMIT}-{label}-Release"
    archive = cache / f"{name}.tar.gz"
    download(f"https://github.com/google/dawn/releases/download/{DAWN_TAG}/{archive.name}", archive, digest)
    if not (cache / name).is_dir():
        with tarfile.open(archive) as source:
            source.extractall(cache, filter="data")
    dawn_cmake = next((cache / name).glob("lib*/cmake/Dawn"))
    build = cache / "build"
    # Prevent repo-wide .NET props/targets from being imported by generated VCX projects.
    for filename in ("Directory.Build.props", "Directory.Build.targets"):
        (cache / filename).write_text("<Project/>\n")
    cmake = ["uv", "run", "--with", "cmake==4.4.4", "cmake"]
    subprocess.run(
        [
            *cmake,
            "-S",
            str(boundary),
            "-B",
            str(build),
            f"-DDawn_DIR={dawn_cmake}",
            f"-DCMAKE_INSTALL_PREFIX={sdk}",
            "-DCMAKE_BUILD_TYPE=Release",
            *(["-A", "x64"] if platform == "windows" else []),
        ],
        check=True,
    )
    subprocess.run([*cmake, "--build", str(build), "--config", "Release"], check=True)
    subprocess.run([*cmake, "--install", str(build), "--config", "Release"], check=True)
    notices = sdk / "licenses"
    notices.mkdir(exist_ok=True)
    for source in (ROOT / "nuget/webgpu").iterdir():
        shutil.copy2(source, notices / source.name)
    if platform == "windows":
        dxc = cache / DXC_NAME
        download(
            f"https://github.com/microsoft/DirectXShaderCompiler/releases/download/{DXC_TAG}/{DXC_NAME}",
            dxc,
            DXC_SHA256,
        )
        with zipfile.ZipFile(dxc) as source:
            for filename in ("dxcompiler.dll", "dxil.dll"):
                (sdk / "bin" / filename).write_bytes(source.read(f"bin/x64/{filename}"))
            for name in source.namelist():
                if "license" in name.lower() and not name.endswith("/"):
                    (notices / ("DXC-" + Path(name).name)).write_bytes(source.read(name))
    stamp.write_text(json.dumps(expected, indent=2) + "\n")
    return sdk


def stage_native(sdk, output, platform, arch):
    rid = f"{RID[platform]}-{'x64' if arch == 'x86_64' else arch}"
    destination = output / "webgpu" / rid
    destination.mkdir(parents=True, exist_ok=True)
    runtime = sdk / RUNTIME[platform]
    files = [runtime]
    if platform == "windows":
        files += [sdk / "bin" / name for name in ("dxcompiler.dll", "dxil.dll")]
    for source in files:
        if not source.is_file():
            raise ValueError(f"Missing WebGPU runtime dependency: {source}")
        shutil.copy2(source, destination / source.name)
        shutil.copy2(source, output / source.name)
    if not (sdk / "licenses").is_dir():
        raise ValueError("Native Dawn SDK must contain licenses/ for distribution")
    shutil.copytree(sdk / "licenses", destination / "licenses", dirs_exist_ok=True)


def stage_web_port(destination):
    source = ROOT / "godot/thirdparty/emdawnwebgpu"
    shutil.copytree(
        source, destination / "emdawnwebgpu", dirs_exist_ok=True, ignore=shutil.ignore_patterns("__pycache__", "*.pyc")
    )
