#!/usr/bin/env python3

import argparse
import os
import platform
import subprocess
import sys
import time
from dataclasses import dataclass
from enum import Enum

import rich.markup
from rich.console import Console
from rich.live import Live
from rich.panel import Panel
from rich.table import Table
from rich.text import Text

console = Console()


class Platform(Enum):
    WINDOWS = "windows"
    LINUX = "linuxbsd"
    MACOS = "macos"


class Arch(Enum):
    X86_64 = "x86_64"
    ARM64 = "arm64"


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


def get_platform_config(platform_override: str | None = None, arch_override: str | None = None) -> PlatformConfig:
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

    if plat == Platform.WINDOWS:
        # Absolute path from repo root (works regardless of cwd)
        return PlatformConfig(
            godot_platform=Platform.WINDOWS.value,
            godot_arch=arch_str,
            godot_exe=os.path.abspath(f"godot/bin/godot.windows.editor.dev.{arch_str}.executable.mono.exe"),
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
            godot_exe=f"./bin/godot.linuxbsd.editor.dev.{arch_str}.executable.mono",
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
            godot_exe=f"./bin/godot.macos.editor.dev.{arch_str}.executable.mono",
            lib_path_var="DYLD_LIBRARY_PATH",
            path_separator=":",
            lib_extension=".dylib",
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
        default="yes",
        help="Enable development build features",
    )
    parser.add_argument(
        "--debug-symbols",
        type=str,
        choices=["yes", "no"],
        default="yes",
        help="Include debug symbols in build",
    )
    parser.add_argument(
        "--scu-build",
        type=str,
        choices=["yes", "no"],
        default="yes",
        help="Enable Single Compilation Unit build",
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

    parser.add_argument(
        "--no-restore",
        action="store_true",
        help="Skip dotnet clean & restore",
    )

    # Platform/architecture overrides for CI
    parser.add_argument(
        "--platform",
        type=str,
        choices=["auto", "windows", "linuxbsd", "macos"],
        default="auto",
        help="Override target platform (for CI/cross-compilation)",
    )
    parser.add_argument(
        "--arch",
        type=str,
        choices=["auto", "x86_64", "arm64"],
        default="auto",
        help="Override target architecture (for CI/cross-compilation)",
    )
    parser.add_argument(
        "--target",
        type=str,
        choices=["all", "template_release", "editor"],
        default="all",
        help="Build specific libgodot target only (for CI)",
    )

    return parser.parse_args()


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
                    display.append("⚙ ", style="bold cyan")
                    display.append(f"{description}", style="bold cyan")
                    display.append(f" [{mins:02d}:{secs:02d}]", style="dim cyan")
                    display.append("\n  ")
                    display.append(last_line[:120], style="dim")  # Limit line length

                    live.update(display)

        process.wait()

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
            sys.exit(1)

        console.print(
            f"[bold green]✓[/bold green] {description} "
            f"[dim cyan]({mins:02d}:{secs:02d})[/dim cyan]"
        )


def show_build_config(args, platform_config: PlatformConfig):
    """Display build configuration in a nice table."""
    table = Table(title="Build Configuration", show_header=True, header_style="bold magenta")
    table.add_column("Setting", style="cyan")
    table.add_column("Value", style="green")

    table.add_row("Platform", platform_config.godot_platform)
    table.add_row("Architecture", platform_config.godot_arch)
    table.add_row("Dev Build", args.dev_build)
    table.add_row("Debug Symbols", args.debug_symbols)
    table.add_row("SCU Build", args.scu_build)
    table.add_row("Godot Executable", platform_config.godot_exe)

    # Build steps
    table.add_row("─" * 30, "─" * 30)
    table.add_row("Skip Editor Build", "Yes" if args.no_editor else "No")
    table.add_row("Skip Glue Generation", "Yes" if args.no_glue else "No")
    table.add_row("Skip Library Build", "Yes" if args.no_library else "No")
    table.add_row("Skip dotnet clean & restore", "Yes" if args.no_restore else "No")

    console.print(table)
    console.print()


def build_editor(args, platform_config: PlatformConfig):
    """Build Godot executable."""
    console.print("\n[bold yellow]┌── Building Godot Editor ──┐[/bold yellow]")
    task_desc = "Building Godot Editor (with mono)"
    run_with_live_output(
        [
            "scons",
            f"platform={platform_config.godot_platform}",
            f"arch={platform_config.godot_arch}",
            "target=editor",
            "module_mono_enabled=yes",
            "extra_suffix=executable",
            "d3d12=no",
            f"dev_build={args.dev_build}",
            f"scu_build={args.scu_build}",
            "debug_symbols=true",
            "separate_debug_symbols=true",
        ],
        cwd="godot",
        description=task_desc,
    )
    console.print(f"[bold green]✓[/bold green] {task_desc}")


def build_libgodot(args, platform_config: PlatformConfig):
    """Build the libgodot library."""
    console.print("\n[bold yellow]┌── Building libgodot ──┐[/bold yellow]")

    # Determine which targets to build
    if args.target == "all":
        targets = ["template_release", "editor"]
    else:
        targets = [args.target]

    for target in targets:
        task_desc = (
            "Building libgodot ("  # noqa: W503
            f"target={target}, platform={platform_config.godot_platform}, "
            f"arch={platform_config.godot_arch})"
        )

        run_with_live_output(
            [
                "scons",
                f"platform={platform_config.godot_platform}",
                f"arch={platform_config.godot_arch}",
                f"target={target}",
                "module_mono_enabled=yes",
                "d3d12=no",
                "library_type=shared_library",
                "extra_suffix=shared_library",
                f"dev_build={args.dev_build}",
                f"scu_build={args.scu_build}",
                f"debug_symbols={args.debug_symbols}",
                f"separate_debug_symbols={args.debug_symbols}",
            ],
            cwd="godot",
            description=task_desc,
        )
        console.print(f"[bold green]✓[/bold green] {task_desc}")


def generate_glue(platform_config: PlatformConfig):
    """Generate Mono glue files."""
    console.print("\n[bold yellow]┌── Generating Mono Glue ──┐[/bold yellow]")

    task_desc = "Creating NuGet packages directory"
    os.makedirs("godot/bin/GodotSharp/Tools/nupkgs", exist_ok=True)
    console.print(f"[bold green]✓[/bold green] {task_desc}")

    task_desc = "nuget locals"
    run_with_live_output(
        ["dotnet", "nuget", "locals", "all", "--clear"],
        description=task_desc,
    )

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
    console.print(f"[bold green]✓[/bold green] {task_desc}")

    task_desc = "Building C# assemblies and NuGet packages"
    run_with_live_output(
        [
            "python",
            "./modules/mono/build_scripts/build_assemblies.py",
            "--godot-platform",
            platform_config.godot_platform,
            "--godot-output-dir",
            "./bin",
            "--push-nupkgs-local",
            "./bin/packages",
        ],
        cwd="godot",
        description=task_desc,
    )
    console.print(f"[bold green]✓[/bold green] {task_desc}")


def restore_dependencies(platform_config: PlatformConfig):
    """Restore .NET Dependencies."""
    console.print("\n[bold yellow]┌── Restoring .NET Dependencies──┐[/bold yellow]")

    task_desc = "dotnet restore"
    run_with_live_output(
        ["dotnet", "restore", "-v", "detailed"],
        description=task_desc,
    )
    console.print(f"[bold green]✓[/bold green] {task_desc}")

    task_desc = "dotnet clean"
    run_with_live_output(
        ["dotnet", "clean", "-v", "detailed"],
        description=task_desc,
    )
    console.print(f"[bold green]✓[/bold green] {task_desc}")

    task_desc = "Generating game UID cache"
    run_with_live_output(
        [
            platform_config.godot_exe,
            "--path",
            "../project",
            "--import",
            "--headless",
        ],
        cwd="godot",
        description=task_desc,
    )
    console.print(f"[bold green]✓[/bold green] {task_desc}")


def main():
    args = parse_arguments()
    platform_config = get_platform_config(
        platform_override=args.platform,
        arch_override=args.arch,
    )

    console.print(
        Panel.fit(
            "[bold white]2dog[/bold white] [bold cyan]libgodot + GodotSharp Build System[/bold cyan]\n"
            "[dim]Building custom Godot engine with C# support[/dim]",
            border_style="cyan",
        )
    )
    console.print()

    show_build_config(args, platform_config)

    if not args.no_editor:
        build_editor(args, platform_config)

    if not args.no_glue:
        generate_glue(platform_config)

    if not args.no_library:
        build_libgodot(args, platform_config)

    if not args.no_restore:
        restore_dependencies(platform_config)

    # Final success message
    console.print()
    console.print(
        Panel.fit(
            "[bold green]✓ Build Complete![/bold green]\n\n"
            "Run the project using:\n"
            "[cyan]  dotnet run --project demo[/cyan]",
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
