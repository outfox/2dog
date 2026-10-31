#!/usr/bin/env python3

import argparse
import os
import platform
from pathlib import Path
import subprocess
import sys
import time
from dataclasses import dataclass
from enum import Enum

from scripts.dotnet_build import get_job_limit
from scripts import webgpu

if sys.platform == "win32":
    os.system("chcp 65001 > NUL")
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

import rich.markup
from rich.console import Console
from rich.live import Live
from rich.panel import Panel
from rich.table import Table
from rich.text import Text

console = Console()
global_start_time = time.time()

class Platform(Enum):
    WINDOWS = "windows"
    LINUX = "linuxbsd"
    MACOS = "macos"
    WEB = "web"
    ANDROID = "android"
    IOS = "ios"
    SDL3 = "sdl3"


class Arch(Enum):
    X86_64 = "x86_64"
    ARM64 = "arm64"
    WASM32 = "wasm32"


@dataclass
class PlatformConfig:
    """Platform-specific build configuration."""

    godot_platform: str
    godot_arch: str
    godot_exe: str
    lib_path_var: str
    path_separator: str
    lib_extension: str
    lib_prefix: str


def detect_platform() -> Platform:
    """Auto-detect the current platform."""
    system = platform.system()
    if system == "Windows":
        return Platform.WINDOWS
    if system == "Linux":
        return Platform.LINUX
    if system == "Darwin":
        return Platform.MACOS

    console.print(f"[bold red]Unsupported platform: {system}[/bold red]")
    sys.exit(1)


def detect_arch() -> Arch:
    """Auto-detect the current architecture."""
    machine = platform.machine().lower()
    if machine in ("x86_64", "amd64"):
        return Arch.X86_64
    if machine in ("arm64", "aarch64"):
        return Arch.ARM64

    console.print(f"[bold yellow]Unknown architecture '{machine}', defaulting to x86_64[/bold yellow]")
    return Arch.X86_64


def get_platform_config(platform_override: str | None = None, arch_override: str | None = None, precision: str = "single") -> PlatformConfig:
    """Get platform configuration, with optional overrides for CI/cross-compilation."""

    # Determine platform
    if platform_override and platform_override != "auto":
        try:
            plat = Platform(platform_override)
        except ValueError:
            console.print(f"[bold red]Invalid platform: {platform_override}[/bold red]")
            console.print(f"Valid options: {', '.join(p.value for p in Platform)}")
            sys.exit(1)
    else:
        plat = detect_platform()

    # Determine architecture
    if arch_override and arch_override != "auto":
        try:
            arch = Arch(arch_override)
        except ValueError:
            console.print(f"[bold red]Invalid architecture: {arch_override}[/bold red]")
            console.print(f"Valid options: {', '.join(a.value for a in Arch)}")
            sys.exit(1)
    else:
        arch = detect_arch()

    arch_str = arch.value
    precision_suffix = ".double" if precision == "double" else ""

    if plat == Platform.ANDROID:
        # Prefer devices over the build machine's architecture when cross-compiling.
        arch_str = Arch.ARM64.value if not arch_override or arch_override == "auto" else arch.value
        if arch_str not in (Arch.ARM64.value, Arch.X86_64.value):
            raise ValueError("Android .NET hosts support arm64 and x86_64 only")
        return PlatformConfig(
            godot_platform=plat.value, godot_arch=arch_str, godot_exe="",
            lib_path_var="PATH", path_separator=os.pathsep,
            lib_extension=".so", lib_prefix="libgodot",
        )

    if plat == Platform.IOS:
        # Static archive linked into the .NET iOS app (device or, with --simulator, the Simulator slice); built
        # on macOS with Xcode only, arm64 only. No editor or glue: both come from the desktop build.
        arch_str = Arch.ARM64.value if not arch_override or arch_override == "auto" else arch.value
        if arch_str != Arch.ARM64.value:
            raise ValueError("iOS .NET hosts support arm64 only")
        return PlatformConfig(
            godot_platform=plat.value, godot_arch=arch_str, godot_exe="",
            lib_path_var="DYLD_LIBRARY_PATH", path_separator=":",
            lib_extension=".a", lib_prefix="libgodot",
        )

    if plat == Platform.WINDOWS:
        # Absolute path from repo root (works regardless of cwd)
        return PlatformConfig(
            godot_platform=Platform.WINDOWS.value,
            godot_arch=arch_str,
            godot_exe=os.path.abspath(f"godot/bin/godot.windows.editor{precision_suffix}.{arch_str}.executable.mono.exe"),
            lib_path_var="PATH",
            path_separator=";",
            lib_extension=".dll",
            lib_prefix="godot",
        )

    if plat == Platform.LINUX:
        # Relative to godot/ directory (we run with cwd="godot")
        return PlatformConfig(
            godot_platform=Platform.LINUX.value,
            godot_arch=arch_str,
            godot_exe=f"./bin/godot.linuxbsd.editor{precision_suffix}.{arch_str}.executable.mono",
            lib_path_var="LD_LIBRARY_PATH",
            path_separator=":",
            lib_extension=".so",
            lib_prefix="libgodot",
        )

    if plat == Platform.MACOS:
        # Relative to godot/ directory (we run with cwd="godot")
        return PlatformConfig(
            godot_platform=Platform.MACOS.value,
            godot_arch=arch_str,
            godot_exe=f"./bin/godot.macos.editor{precision_suffix}.{arch_str}.executable.mono",
            lib_path_var="DYLD_LIBRARY_PATH",
            path_separator=":",
            lib_extension=".dylib",
            lib_prefix="libgodot",
        )

    if plat == Platform.SDL3:
        # A single SDL3 port serves desktop and the consoles: windowing, input and
        # surfaces all come from SDL, so the engine carries no platform-specific
        # OS or DisplayServer for any of them.
        return PlatformConfig(
            godot_platform=Platform.SDL3.value,
            godot_arch=arch_str,
            godot_exe="",  # No editor build; glue comes from the desktop editor build.
            lib_path_var="PATH" if platform.system() == "Windows" else "LD_LIBRARY_PATH",
            path_separator=";" if platform.system() == "Windows" else ":",
            lib_extension=".dll" if platform.system() == "Windows" else ".so",
            lib_prefix="libgodot",
        )

    if plat == Platform.WEB:
        # Emscripten static library (linked into the consumer's
        # dotnet.native.wasm at publish time); there is no web editor and the
        # arch is always wasm32.
        return PlatformConfig(
            godot_platform=Platform.WEB.value,
            godot_arch=Arch.WASM32.value,
            godot_exe="",  # No editor build on web.
            lib_path_var="PATH",
            path_separator=";" if platform.system() == "Windows" else ":",
            lib_extension=".a",
            lib_prefix="libgodot",
        )

    console.print(f"[bold red]Unsupported platform: {plat}[/bold red]")
    sys.exit(1)


def parse_arguments():
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(
        description="2dog libgodot + GodotSharp Build System",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )

    # Build configuration
    parser.add_argument(
        "--dev-build",
        type=str,
        choices=["yes", "no"],
        default="no",
        help="Enable development build features",
    )
    parser.add_argument(
        "--debug-symbols",
        type=str,
        choices=["yes", "no"],
        default="no",
        help="Include debug symbols in build",
    )
    parser.add_argument(
        "--scu-build",
        type=str,
        choices=["yes", "no"],
        default="yes",
        help="Enable Single Compilation Unit build",
    )

    parser.add_argument(
        "--precision", choices=["single", "double"], default="single",
        help="Floating-point precision; double builds require matching .double packages",
    )
    parser.add_argument(
        "--threads", action="store_true",
        help="Web only: multithreaded payload for WasmEnableThreads hosts, staged under bin/web-threads/",
    )
    parser.add_argument("--webgpu", choices=["auto", "yes", "no"], default="auto",
                        help="Include WebGPU (auto: single-precision desktop and single-threaded web)")
    parser.add_argument("--webgpu-native-path", default="",
                        help="Use an existing Dawn boundary SDK instead of preparing the pinned SDK")
    parser.add_argument(
        "--simulator", action="store_true",
        help="iOS only: build the Simulator slice (iossimulator-arm64) instead of the device archive",
    )

    # Build steps control
    parser.add_argument(
        "--no-library",
        action="store_true",
        help="Skip building libgodot library",
    )

    parser.add_argument(
        "--no-editor",
        action="store_true",
        help="Skip compiling the Editor",
    )

    parser.add_argument(
        "--no-glue",
        action="store_true",
        help="Skip generating Mono glue",
    )

    # Platform/architecture overrides for CI
    parser.add_argument(
        "--platform",
        type=str,
        choices=["auto", "windows", "linuxbsd", "macos", "web", "android", "ios", "sdl3"],
        default="auto",
        help="Override target platform (for CI/cross-compilation)",
    )
    parser.add_argument(
        "--arch",
        type=str,
        choices=["auto", "x86_64", "arm64", "wasm32"],
        default="auto",
        help="Override target architecture (for CI/cross-compilation)",
    )
    parser.add_argument(
        "--target",
        type=str,
        choices=["all", "template_release", "template_debug", "editor"],
        default="all",
        help="Build specific libgodot target only (for CI)",
    )
    parser.add_argument(
        "--cache-path",
        type=str,
        default="",
        help="SCons cache directory path (passed as cache_path= to scons)",
    )

    args = parser.parse_args()
    if args.platform == Platform.ANDROID.value and args.arch not in (
        "auto", Arch.ARM64.value, Arch.X86_64.value,
    ):
        parser.error("Android .NET hosts support --arch arm64 or x86_64 only (or auto)")
    if args.platform == Platform.IOS.value and args.arch not in ("auto", Arch.ARM64.value):
        parser.error("iOS .NET hosts support --arch arm64 only (or auto)")
    if args.threads and args.platform != Platform.WEB.value:
        parser.error("--threads requires --platform web")
    if args.simulator and args.platform != Platform.IOS.value:
        parser.error("--simulator requires --platform ios")
    return args


def run_with_live_output(cmd, cwd=None, description="Running command..."):
    """Run a subprocess with live output streaming and elapsed time."""
    start_time = time.time()
    all_output: list[str] = []  # Capture all output for error reporting

    # Create a display for the running command
    with Live(console=console, refresh_per_second=4) as live:
        process = subprocess.Popen(
            cmd,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1,
            universal_newlines=True,
        )

        # Stream output
        for line in iter(process.stdout.readline, ""):
            if line:
                line_stripped = line.rstrip()
                all_output.append(line_stripped)

                if line_stripped:
                    last_line = line_stripped
                    elapsed = time.time() - start_time
                    mins, secs = divmod(int(elapsed), 60)

                    # Update display
                    display = Text()
                    display.append("+ ", style="bold cyan")
                    display.append(f"{description}", style="bold cyan")
                    display.append(f" [{mins:02d}:{secs:02d}]", style="dim cyan")
                    display.append("\n  ")
                    display.append(last_line[:120], style="dim")  # Limit line length

                    live.update(display)

        process.wait()

        # Final display update
        display = Text()
        display.append("  ", style="bold cyan")
        display.append(f"{description}", style="bold cyan")
        display.append(f" [{mins:02d}:{secs:02d}]", style="dim cyan")
        display.append("\n  ")
        display.append(last_line[:120], style="dim")  # Limit line length

        live.update(display)
        live.stop()

        elapsed = time.time() - start_time
        mins, secs = divmod(int(elapsed), 60)

        if process.returncode != 0:
            console.print(
                f"[bold red]✗ Failed:[/bold red] {description} "
                f"[dim cyan]({mins:02d}:{secs:02d})[/dim cyan]"
            )
            console.print()
            console.print(
                Panel(
                    rich.markup.escape("\n".join(all_output[-50:])),  # Show last 50 lines
                    title="[bold red]Error Output (last 50 lines)[/bold red]",
                    border_style="red",
                )
            )
            sys.exit(process.returncode)

        total_elapsed = time.time() - global_start_time
        mins, secs = divmod(int(total_elapsed), 60)
        console.print(
            f"[bold green]✓[/bold green] {description} "
            f"[dim cyan]({mins:02d}:{secs:02d})[/dim cyan]"
        )


def get_scons_job_limit():
    value = os.environ.get("DOTNET_PROCESSOR_COUNT")
    if value is None:
        return get_job_limit()
    limit = int(value)
    if limit < 1:
        raise ValueError("DOTNET_PROCESSOR_COUNT must be a positive integer")
    return min(limit, get_job_limit() or os.cpu_count() or 1)


def show_build_config(args, platform_config: PlatformConfig):
    """Display build configuration in a nice table."""
    table = Table(title="Build Configuration", show_header=True, header_style="bold magenta")
    table.add_column("Setting", style="cyan")
    table.add_column("Value", style="green")

    table.add_row("Platform", platform_config.godot_platform)
    table.add_row("Architecture", platform_config.godot_arch)
    table.add_row("Precision", getattr(args, "precision", "single"))
    if args.threads:
        table.add_row("Threads", "yes (proxy_to_pthread)")
    if getattr(args, "simulator", False):
        table.add_row("iOS slice", "Simulator (iossimulator-arm64)")
    table.add_row("Maximum build jobs", str(get_scons_job_limit() or "SCons default"))
    table.add_row("Debug Symbols (separate)", args.debug_symbols)
    table.add_row("SCU Build", args.scu_build)
    table.add_row("Dev Build", args.dev_build)

    # Build steps
    table.add_row("─" * 30, "─" * 11)
    table.add_row("Skip Editor Build", "Yes" if args.no_editor else "No")
    table.add_row("Skip Glue Generation", "Yes" if args.no_glue else "No")
    table.add_row("Skip Library Build", "Yes" if args.no_library else "No")

    console.print(table)
    console.print()


def build_editor(args, platform_config: PlatformConfig):
    """Build Godot executable."""
    console.print("\n[bold yellow]┌── Building Godot Editor ──┐[/bold yellow]")
    task_desc = "Building Godot Editor"
    cmd = [
        "scons",
        *([f"--jobs={get_scons_job_limit()}"] if get_scons_job_limit() is not None else []),
        f"platform={platform_config.godot_platform}",
        f"arch={platform_config.godot_arch}",
        "target=editor",
        "module_mono_enabled=yes",
        f"precision={getattr(args, 'precision', 'single')}",
        "extra_suffix=executable",
        "d3d12=no",
        f"dev_build={args.dev_build}",
        f"scu_build={args.scu_build}",
        "debug_symbols=true",
        "separate_debug_symbols=true",
    ]
    if args.cache_path:
        cmd.append(f"cache_path={args.cache_path}")
    cmd.extend(webgpu_flags(args, platform_config))
    run_with_live_output(cmd, cwd="godot", description=task_desc)


def webgpu_flags(args, config):
    if not webgpu.enabled(args, config.godot_platform):
        return ["webgpu=no"]
    flags = ["webgpu=yes", "webgpu_precompile=no"]
    if config.godot_platform != "web":
        if (config.godot_platform, config.godot_arch) not in webgpu.DAWN_ARCHIVES and not getattr(args, "webgpu_native_path", ""):
            if getattr(args, "webgpu", "auto") == "yes":
                raise ValueError("This architecture requires --webgpu-native-path with a matching Dawn boundary SDK")
            return ["webgpu=no"]
        sdk = Path(args.webgpu_native_path).resolve() if getattr(args, "webgpu_native_path", "") else webgpu.prepare_native(config.godot_platform, config.godot_arch)
        flags.append(f"webgpu_native_path={sdk.as_posix()}")
        webgpu.stage_native(sdk, native_output_dir(getattr(args, "precision", "single")), config.godot_platform, config.godot_arch)
    return flags


def read_pinned_emscripten_version() -> str:
    """Read <EmscriptenVersion> from Directory.Build.props (the version the
    .NET browser-wasm runtime pack was built with)."""
    import re

    props = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Directory.Build.props")
    with open(props, encoding="utf-8") as f:
        m = re.search(r"<EmscriptenVersion>([^<]+)</EmscriptenVersion>", f.read())
    return m.group(1) if m else ""


def check_emscripten_version():
    """Warn if the local emcc does not match the pinned version. The web
    libgodot.a is statically linked into dotnet.native.wasm, so the
    toolchains must agree."""
    pinned = read_pinned_emscripten_version()
    if not pinned:
        console.print("[bold yellow]No <EmscriptenVersion> pin found in Directory.Build.props[/bold yellow]")
        return
    try:
        out = subprocess.run(["emcc", "--version"], capture_output=True, text=True, shell=(sys.platform == "win32"))
        first_line = out.stdout.splitlines()[0] if out.stdout else ""
    except FileNotFoundError:
        console.print("[bold red]emcc not found on PATH - install/activate emsdk " f"{pinned}[/bold red]")
        sys.exit(1)
    if pinned not in first_line:
        console.print(
            f"[bold yellow]WARNING: emcc version mismatch - pinned {pinned}, found: {first_line}\n"
            "The web libgodot.a must be built with the same emscripten as the .NET runtime pack.[/bold yellow]"
        )


def build_libgodot_web(args):
    """Build the web (emscripten) static library and assemble the packaging
    payload under godot/bin/web/<target>/."""
    console.print("\n[bold yellow]┌── Building libgodot (web static library) ──┐[/bold yellow]")

    check_emscripten_version()

    if args.target == "all":
        targets = ["template_release", "template_debug"]
    elif args.target == "editor":
        console.print("[bold red]The web platform has no editor target.[/bold red]")
        sys.exit(1)
    else:
        targets = [args.target]

    web_dir = "web-threads" if args.threads else "web"
    if getattr(args, "precision", "single") == "double":
        web_dir = os.path.join("double", web_dir)

    import shutil

    for target in targets:
        # scons only ever adds to the .web_zip staging dir; wipe it so files
        # from a previous module set don't leak into this payload.
        stale_zip = os.path.join("godot", "bin", ".web_zip")
        if os.path.isdir(stale_zip):
            shutil.rmtree(stale_zip)
        task_desc = f"Building libgodot (target={target}, platform=web, arch=wasm32)"
        cmd = [
            "scons",
            *([f"--jobs={get_scons_job_limit()}"] if get_scons_job_limit() is not None else []),
            "platform=web",
            "arch=wasm32",
            f"target={target}",
            "module_mono_enabled=yes",
            f"precision={getattr(args, 'precision', 'single')}",
            "library_type=static_library",
            "extra_suffix=static_library",
            # .NET's threaded runtime runs Main, and so Godot's main loop, on a worker (the deputy thread);
            # mono's config requires proxy_to_pthread to match.
            *(["threads=yes", "proxy_to_pthread=yes"] if args.threads else ["threads=no"]),
            # LTO is blocked for web+mono: modules/mono/config.py rejects it,
            # and emscripten < 4.0.9 (the pinned 3.1.56 matches the .NET
            # runtime pack) has no thin-LTO. Revisit when the runtime pack's
            # emscripten moves to 4.x.
            "lto=none",
            "disable_crash_handler=yes",
            "dev_build=no",
            f"scu_build={args.scu_build}",
            # Allow --path override at runtime (needed for libgodot to load projects)
            "disable_path_overrides=no",
        ]
        if args.cache_path:
            cmd.append(f"cache_path={args.cache_path}")
        cmd.extend(webgpu_flags(args, get_platform_config("web", "wasm32")))
        run_with_live_output(cmd, cwd="godot", description=task_desc)

        # Assemble the per-target packaging payload:
        #   web/<target>/libgodot/  - static lib + emcc config + js glue
        #                             (from the scons-staged template zip dir)
        #   web/<target>/shell/     - the Godot engine boot shell the page
        #                             loads (godot.js wraps mono_bridge +
        #                             engine.js; plus audio worklets)
        zip_dir = os.path.join("godot", "bin", ".web_zip")
        src = os.path.join(zip_dir, "libgodot")
        dst = os.path.join("godot", "bin", web_dir, target)
        if not os.path.isfile(os.path.join(src, "libgodot.a")):
            console.print(f"[bold red]Expected web payload not found at {src}[/bold red]")
            sys.exit(1)
        if os.path.isdir(dst):
            shutil.rmtree(dst)
        shutil.copytree(src, os.path.join(dst, "libgodot"))
        if webgpu.enabled(args, "web"):
            webgpu.stage_web_port(Path(dst) / "libgodot")
        shell_dst = os.path.join(dst, "shell")
        os.makedirs(shell_dst)
        for shell_file in ["godot.js", "godot.audio.worklet.js", "godot.audio.position.worklet.js"]:
            shutil.copy2(os.path.join(zip_dir, shell_file), shell_dst)
        console.print(f"[green]Web payload staged: {dst}[/green]")


def build_libgodot(args, platform_config: PlatformConfig):
    """Build the libgodot library."""
    if platform_config.godot_platform == Platform.WEB.value:
        build_libgodot_web(args)
        return

    console.print("\n[bold yellow]┌── Building libgodot ──┐[/bold yellow]")

    # Determine which targets to build
    is_ios = platform_config.godot_platform == Platform.IOS.value
    if args.target == "all":
        targets = ["template_release", "template_debug"]
        if platform_config.godot_platform not in (Platform.ANDROID.value, Platform.SDL3.value) and not is_ios:
            targets.append("editor")
    else:
        targets = [args.target]

    if platform_config.godot_platform == Platform.SDL3.value and "editor" in targets:
        # --target all asks for the editor too; skip it rather than fail, but say why.
        console.print("[yellow]sdl3 has no editor target; glue still comes from a desktop editor build.[/yellow]")
        targets = [t for t in targets if t != "editor"]
        raise ValueError("Android .NET library builds have no editor target; use a desktop editor for glue")
    if is_ios and "editor" in targets:
        raise ValueError("iOS .NET library builds have no editor target; use a desktop editor for glue")

    for target in targets:
        # template_release should never be a dev build (for optimized release binaries)
        # editor target uses the configurable dev_build setting
        use_dev_build = "no" if target == "template_release" else args.dev_build

        task_desc = (
            "Building libgodot ("  # noqa: W503
            f"target={target}, platform={platform_config.godot_platform}, "
            f"arch={platform_config.godot_arch}, dev_build={use_dev_build})"
        )

        cmd = [
            "scons",
            *([f"--jobs={get_scons_job_limit()}"] if get_scons_job_limit() is not None else []),
            f"platform={platform_config.godot_platform}",
            f"arch={platform_config.godot_arch}",
            f"target={target}",
            "module_mono_enabled=yes",
            f"precision={getattr(args, 'precision', 'single')}",
            "d3d12=no",
            # iOS is always a static archive; the library_type adds the libgodot entry points (platform/ios/libgodot_ios.mm).
            "library_type=static_library" if is_ios else "library_type=shared_library",
            *([] if is_ios else ["extra_suffix=shared_library"]),
            # Godot's Metal driver, no MoltenVK; the Simulator has neither and falls back to GLES3. iOS 15.0 is the
            # hosts' SupportedOSPlatformVersion and the floor this SDK's libc++ still supports.
            *(["vulkan=no", "generate_bundle=no", "ios_min_version=15.0", f"simulator={'yes' if getattr(args, 'simulator', False) else 'no'}"] if is_ios else []),
            f"dev_build={use_dev_build}",
            f"scu_build={args.scu_build}",
            f"debug_symbols={args.debug_symbols}",
            f"separate_debug_symbols={args.debug_symbols}",
            # Allow --path override at runtime (needed for libgodot to load projects)
            "disable_path_overrides=no",
        ]
        if args.cache_path:
            cmd.append(f"cache_path={args.cache_path}")
        cmd.extend(webgpu_flags(args, platform_config))
        run_with_live_output(cmd, cwd="godot", description=task_desc)

        if platform_config.godot_platform == Platform.ANDROID.value:
            # Android's SCsub moves the build into java/lib/libs, including the NDK's C++ runtime.
            # Preserve libgodot_android.so: GodotLib.java and the ELF SONAME depend on this name.
            import shutil
            from pathlib import Path

            variant = "release" if target == "template_release" else "debug"
            abi = "arm64-v8a" if platform_config.godot_arch == "arm64" else "x86_64"
            source = Path("godot/platform/android/java/lib/libs") / variant / abi
            destination = native_output_dir(getattr(args, "precision", "single")) / "android" / target / platform_config.godot_arch
            destination.mkdir(parents=True, exist_ok=True)
            for name in ("libgodot_android.so", "libc++_shared.so"):
                shutil.copy2(source / name, destination / name)

        elif is_ios:
            # Stage the archive per RID, where the 2dog.ios-* packages and resolver look for it.
            import shutil
            from pathlib import Path

            simulator = getattr(args, "simulator", False)
            rid = "iossimulator-arm64" if simulator else "ios-arm64"
            name = ios_archive_name(target, use_dev_build == "yes", getattr(args, "precision", "single"), simulator)
            destination = native_output_dir(getattr(args, "precision", "single")) / "ios" / target / rid
            destination.mkdir(parents=True, exist_ok=True)
            shutil.copy2(Path("godot/bin") / name, destination / "libgodot.a")

        elif platform_config.godot_platform == Platform.SDL3.value:
            # sdl3 keeps its own artifact name. Staging under the desktop platform's
            # name would silently replace a working engine with an experimental one,
            # and `pack platforms` copies that file into the NuGet native payload.
            # Select it with -p:TwoDogSdl3Natives=true.
            import shutil
            from pathlib import Path

            suffix = ".double" if getattr(args, "precision", "single") == "double" else ""
            dev = ".dev" if use_dev_build == "yes" else ""
            # SCons gives shared libraries a `lib` prefix everywhere except Windows.
            source_prefix = "godot" if platform_config.lib_extension == ".dll" else "libgodot"
            source = Path("godot/bin") / (
                f"{source_prefix}.sdl3.{target}{dev}{suffix}.{platform_config.godot_arch}.shared_library{platform_config.lib_extension}"
            )
            destination = native_output_dir(getattr(args, "precision", "single")) / "sdl3" / (
                f"godot.sdl3.{target}{dev}{suffix}.{platform_config.godot_arch}.shared_library{platform_config.lib_extension}"
            )
            destination.parent.mkdir(parents=True, exist_ok=True)
            # copy2 keeps the source mtime, which can be older than the file being
            # replaced; consumers copy by timestamp, so stamp it now.
            shutil.copy(source, destination)
            os.utime(destination, None)
            console.print(f"[green]Staged {destination}[/green]")

        elif getattr(args, "precision", "single") == "double":
            # SCons names double binaries separately; stage all double payloads together.
            import shutil
            from pathlib import Path

            prefix = "godot" if platform_config.godot_platform == "windows" else "libgodot"
            name = (f"{prefix}.{platform_config.godot_platform}.{target}.double."
                    f"{platform_config.godot_arch}.shared_library{platform_config.lib_extension}")
            native_output_dir("double").mkdir(parents=True, exist_ok=True)
            shutil.copy2(Path("godot/bin") / name, native_output_dir("double") / name)


def ios_archive_name(target, dev_build, precision, simulator):
    """SCons names the iOS archive libgodot.ios.<target>[.dev][.double].arm64[.simulator].a."""
    return (f"libgodot.ios.{target}{'.dev' if dev_build else ''}{'.double' if precision == 'double' else ''}"
            f".arm64{'.simulator' if simulator else ''}.a")


def native_output_dir(precision):
    from pathlib import Path
    return Path("godot/bin/double" if precision == "double" else "godot/bin")


def generate_glue(platform_config: PlatformConfig, precision: str = "single"):
    """Generate Mono glue files."""
    console.print("\n[bold yellow]┌── Generating Mono Glue ──┐[/bold yellow]")

    task_desc = "Creating NuGet packages directory"
    os.makedirs("godot/bin/GodotSharp/Tools/nupkgs", exist_ok=True)

    task_desc = "Generating Mono glue files"
    run_with_live_output(
        [
            platform_config.godot_exe,
            "--headless",
            "--generate-mono-glue",
            "./modules/mono/glue",
        ],
        cwd="godot",
        description=task_desc,
    )

    task_desc = "Building C# assemblies and NuGet packages"
    run_with_live_output(
        [
            "uv", "run", "--project", "..", "python",
            "./modules/mono/build_scripts/build_assemblies.py",
            "--godot-platform",
            platform_config.godot_platform,
            "--godot-output-dir",
            "./bin",
            "--precision", precision,
        ],
        cwd="godot",
        description=task_desc,
    )
    if precision == "double":
        import shutil
        shutil.copytree("godot/bin/GodotSharp", "godot/bin/double/GodotSharp", dirs_exist_ok=True)


def main():
    args = parse_arguments()
    # Resolve cache path to absolute so it works regardless of scons cwd
    if args.cache_path:
        args.cache_path = os.path.abspath(args.cache_path)
    platform_config = get_platform_config(
        platform_override=args.platform,
        arch_override=args.arch,
        precision=args.precision,
    )

    if platform_config.godot_platform in (
        Platform.WEB.value,
        Platform.ANDROID.value,
        Platform.IOS.value,
        Platform.SDL3.value,
    ):
        # Editor/glue come from desktop builds for the cross-compilation targets.
        args.no_editor = True
        args.no_glue = True

    show_build_config(args, platform_config)

    console.print(
        Panel.fit(
            f" [bold white]2dog[/bold white] [bold cyan]libgodot and GodotSharp Build System[/bold cyan]\n"
            "[dim]Building custom Godot engine with C# support[/dim]\n"
            "[dim]ETA about [underline] 30 min [/underline] per platform the 1st time[/dim]",
            border_style="cyan",
        )
    )

    if not args.no_editor:
        build_editor(args, platform_config)

    if not args.no_glue:
        generate_glue(platform_config, args.precision)

    if not args.no_library:
        build_libgodot(args, platform_config)

    # Final success message
    console.print()

    total_elapsed = time.time() - global_start_time
    mins, secs = divmod(int(total_elapsed), 60)
    console.print(
        f""
    )

    console.print(
        Panel.fit(
            f"[bold green]✓ 2dog LibGodot Build Complete after {mins:02d}min, {secs:02d}sec\n\n"
            "Run the project, e.g. by using:\n"
            "[cyan]  uv run poe build[/cyan]\n"
            "[cyan]  cd demo[/cyan]\n"
            "[cyan]  dotnet run[/cyan]",
            border_style="green",
        )
    )


if __name__ == "__main__":
    # noinspection PyBroadException
    try:
        main()
    except KeyboardInterrupt:
        console.print("\n[bold red]Build cancelled by user[/bold red]")
        sys.exit(1)
    except Exception as e:  # noqa: F841
        console.print("\n[bold red]Build failed with error:[/bold red]")
        import traceback

        console.print(traceback.format_exc(), markup=False)
        sys.exit(1)
