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


@dataclass
class PlatformConfig:
    """Platform-specific build configuration."""
    godot_platform: str
    godot_exe: str
    engine_exe: str
    lib_path_var: str
    path_separator: str


def get_platform_config() -> PlatformConfig:
    """Detect platform and return appropriate configuration."""
    system = platform.system()

    if system == "Windows":
        return PlatformConfig(
            godot_platform=Platform.WINDOWS.value,
            godot_exe=os.path.abspath("godot/bin/godot.windows.editor.dev.x86_64.executable.mono.exe"),
            engine_exe="engine/bin/Debug/net10.0/engine.exe",
            lib_path_var="PATH",
            path_separator=";"
        )
    elif system == "Linux":
        return PlatformConfig(
            godot_platform=Platform.LINUX.value,
            godot_exe="./bin/godot.linuxbsd.editor.dev.x86_64.executable.mono",
            engine_exe="./engine/bin/Debug/net10.0/engine",
            lib_path_var="LD_LIBRARY_PATH",
            path_separator=":"
        )
    elif system == "Darwin":
        return PlatformConfig(
            godot_platform=Platform.MACOS.value,
            godot_exe="./bin/godot.macos.editor.dev.x86_64.executable.mono",
            engine_exe="./engine/bin/Debug/net10.0/engine",
            lib_path_var="DYLD_LIBRARY_PATH",
            path_separator=":"
        )
    else:
        console.print(f"[bold red]Unsupported platform: {system}[/bold red]")
        sys.exit(1)


def parse_arguments():
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(
        description="2dog libgodot + GodotSharp Build System",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter
    )

    # Build configuration
    parser.add_argument(
        "--dev-build",
        type=str,
        choices=["yes", "no"],
        default="yes",
        help="Enable development build features"
    )
    parser.add_argument(
        "--debug-symbols",
        type=str,
        choices=["yes", "no"],
        default="yes",
        help="Include debug symbols in build"
    )
    parser.add_argument(
        "--scu-build",
        type=str,
        choices=["yes", "no"],
        default="yes",
        help="Enable Single Compilation Unit build"
    )

    # Build steps control
    parser.add_argument(
        "--no-library",
        action="store_true",
        help="Skip building libgodot library"
    )
    parser.add_argument(
        "--no-glue",
        action="store_true",
        help="Skip generating Mono glue"
    )
    parser.add_argument(
        "--no-run",
        action="store_true",
        help="Skip running the driver after build"
    )

    return parser.parse_args()


def run_with_live_output(cmd, cwd=None, description="Running command..."):
    """Run a subprocess with live output streaming and elapsed time."""
    start_time = time.time()
    all_output = []  # Capture all output for error reporting

    # Create a display for the running command
    with Live(console=console, refresh_per_second=4) as live:
        process = subprocess.Popen(
            cmd,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1,
            universal_newlines=True
        )

        ""

        # Stream output
        for line in iter(process.stdout.readline, ''):
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
            console.print(f"[bold red]✗ Failed:[/bold red] {description} [dim cyan]({mins:02d}:{secs:02d})[/dim cyan]")
            console.print()
            console.print(Panel(
                rich.markup.escape("\n".join(all_output[-50:])),  # Show last 50 lines
                title="[bold red]Error Output (last 50 lines)[/bold red]",
                border_style="red"
            ))
            sys.exit(1)

        console.print(f"[bold green]✓[/bold green] {description} [dim cyan]({mins:02d}:{secs:02d})[/dim cyan]")


def show_build_config(args, platform_config):
    """Display build configuration in a nice table."""
    table = Table(title="Build Configuration", show_header=True, header_style="bold magenta")
    table.add_column("Setting", style="cyan")
    table.add_column("Value", style="green")

    table.add_row("Platform", platform_config.godot_platform)
    table.add_row("Dev Build", args.dev_build)
    table.add_row("Debug Symbols", args.debug_symbols)
    table.add_row("SCU Build", args.scu_build)
    table.add_row("Godot Executable", platform_config.godot_exe)
    table.add_row("Engine Executable", platform_config.engine_exe)

    # Build steps
    table.add_row("─" * 30, "─" * 30)
    table.add_row("Skip Library Build", "Yes" if args.no_library else "No")
    table.add_row("Skip Glue Generation", "Yes" if args.no_glue else "No")
    table.add_row("Skip Driver Run", "Yes" if args.no_run else "No")

    console.print(table)
    console.print()


def build_libgodot(args, _):
    """Build the libgodot library."""
    console.print("[bold yellow]┌── Building libgodot ──┐[/bold yellow]")

    for target in ["template_release", "editor"]:
        task_desc = f"Building libgodot (target={target})"

        run_with_live_output(
            ["scons", f"target={target}", "module_mono_enabled=yes",
             "library_type=shared_library", "extra_suffix=shared_library",
             f"dev_build={args.dev_build}", f"scu_build={args.scu_build}",
             f"debug_symbols={args.debug_symbols}", f"separate_debug_symbols={args.debug_symbols}"],
            cwd="godot",
            description=task_desc
        )

    # Build Godot executable
    task_desc = "Building Godot executable"
    run_with_live_output(
        ["scons", "target=editor", "module_mono_enabled=yes", "extra_suffix=executable",
         f"dev_build={args.dev_build}", f"scu_build={args.scu_build}",
         "debug_symbols=true", "separate_debug_symbols=true"],
        cwd="godot",
        description=task_desc
    )


def generate_glue(platform_config):
    """Generate Mono glue files."""
    console.print("\n[bold yellow]┌── Generating Mono Glue ──┐[/bold yellow]")

    task_desc = "Creating NuGet packages directory"
    os.makedirs("godot/bin/GodotSharp/Tools/nupkgs", exist_ok=True)
    console.print(f"[bold green]✓[/bold green] {task_desc}")

    task_desc = "Generating game UID cache"
    run_with_live_output(
        [platform_config.godot_exe, "--path", "../project", "--import", "--headless"],
        cwd="godot",
        description=task_desc
    )

    task_desc = "Generating Mono glue files"
    run_with_live_output(
        [platform_config.godot_exe, "--headless", "--generate-mono-glue", "./modules/mono/glue"],
        cwd="godot",
        description=task_desc
    )

    task_desc = "Building C# assemblies and NuGet packages"
    run_with_live_output(
        ["python", "./modules/mono/build_scripts/build_assemblies.py",
         "--godot-platform", platform_config.godot_platform, "--godot-output-dir", "./bin",
         "--no-deprecated"],
        cwd="godot",
        description=task_desc
    )


def build_engine():
    """Build the engine project."""
    console.print("\n[bold yellow]┌── Building Engine ──┐[/bold yellow]")

    task_desc = "Restoring engine dependencies"
    run_with_live_output(
        ["dotnet", "restore"],
        cwd="engine",
        description=task_desc
    )

    task_desc = "Building engine project"
    run_with_live_output(
        ["dotnet", "build"],
        cwd="engine",
        description=task_desc
    )


def restore_packages():
    """Restore .NET packages for game and project."""
    console.print("\n[bold yellow]┌── Restoring .NET Packages ──┐[/bold yellow]")

    task_desc = "Restoring game dependencies"
    run_with_live_output(
        ["dotnet", "restore"],
        cwd="game",
        description=task_desc
    )

    task_desc = "Restoring project dependencies"
    run_with_live_output(
        ["dotnet", "restore"],
        cwd="project",
        description=task_desc
    )


def main():
    args = parse_arguments()
    platform_config = get_platform_config()

    console.print(Panel.fit(
        "[bold white]2dog[/bold white] [bold cyan]libgodot + GodotSharp Build System[/bold cyan]\n"
        "[dim]Building custom Godot engine with C# support[/dim]",
        border_style="cyan"
    ))
    console.print()

    show_build_config(args, platform_config)

    # Build libgodot
    if not args.no_library:
        build_libgodot(args, platform_config)

    # Generate glue
    if not args.no_glue:
        generate_glue(platform_config)

    # Build engine
    build_engine()

    # Check early exit
    if args.no_run:
        console.print("\n[bold green]✓ Build complete (skipping driver run)![/bold green]")
        return

    # Restore packages
    restore_packages()

    # Final success message
    console.print()
    console.print(Panel.fit(
        "[bold green]✓ Build Complete![/bold green]\n\n"
        "Run the project using:\n"
        "[cyan]  dotnet run --project engine[/cyan]",
        border_style="green"
    ))


if __name__ == "__main__":
    # noinspection PyBroadException
    try:
        main()
    except KeyboardInterrupt:
        console.print("\n[bold red]Build cancelled by user[/bold red]")
        sys.exit(1)
    except Exception as e:
        console.print(f"\n[bold red]Build failed with error:[/bold red]")
        import traceback

        console.print(traceback.format_exc(), markup=False)
        sys.exit(1)