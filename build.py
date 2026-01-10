#!/usr/bin/env python3

import subprocess
import os
import sys
import platform
import shutil
from rich.console import Console
from rich.progress import Progress, SpinnerColumn, TextColumn, TimeElapsedColumn
from rich.panel import Panel
from rich.table import Table
from rich.live import Live
from rich.text import Text
from rich import print as rprint
import time

console = Console()

# Build configuration
dev_build = "yes"
debug_symbols = "yes"
scu_build = "yes"

# Platform-specific settings
is_windows = platform.system() == "Windows"

if is_windows:
    godot_platform = "windows"
    godot_exe = os.path.abspath("godot/bin/godot.linuxbsd.editor.dev.x86_64.executable.mono")
    engine_exe = "engine/bin/Debug/net10.0/engine.exe"
    lib_path_var = "PATH"
    path_separator = ";"
else:
    godot_platform = "linuxbsd"
    godot_exe = "./bin/godot.linuxbsd.editor.dev.x86_64.executable.mono"
    engine_exe = "./engine/bin/Debug/net10.0/engine"
    lib_path_var = "LD_LIBRARY_PATH"
    path_separator = ":"


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

        last_line = ""

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
                    display.append("⠿ ", style="bold cyan")
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
                "\n".join(all_output[-50:]),  # Show last 50 lines
                title="[bold red]Error Output (last 50 lines)[/bold red]",
                border_style="red"
            ))
            sys.exit(1)

        console.print(f"[bold green]✓[/bold green] {description} [dim cyan]({mins:02d}:{secs:02d})[/dim cyan]")


def show_build_config():
    """Display build configuration in a nice table."""
    table = Table(title="Build Configuration", show_header=True, header_style="bold magenta")
    table.add_column("Setting", style="cyan")
    table.add_column("Value", style="green")

    table.add_row("Platform", godot_platform)
    table.add_row("Dev Build", dev_build)
    table.add_row("Debug Symbols", debug_symbols)
    table.add_row("SCU Build", scu_build)
    table.add_row("Godot Executable", godot_exe)
    table.add_row("Engine Executable", engine_exe)

    console.print(table)
    console.print()


def main():
    console.print(Panel.fit(
        "[bold white]2dog[/bold white] [bold cyan]libgodot + GodotSharp Build System[/bold cyan]\n"
        "[dim]Building custom Godot engine with C# support[/dim]",
        border_style="cyan"
    ))
    console.print()

    show_build_config()

    # Build libgodot
    if "--no-build" not in sys.argv:
        console.print("[bold yellow]━━━ Building libgodot ━━━[/bold yellow]")

        for target in ["template_release", "editor"]:
            task_desc = f"Building libgodot (target={target})"

            run_with_live_output(
                ["scons", f"target={target}", "module_mono_enabled=yes",
                 "library_type=shared_library", "extra_suffix=shared_library",
                 f"dev_build={dev_build}", f"scu_build={scu_build}",
                 f"debug_symbols={debug_symbols}", f"separate_debug_symbols={debug_symbols}"],
                cwd="godot",
                description=task_desc
            )

        # Build Godot executable
        task_desc = "Building Godot executable"
        run_with_live_output(
            ["scons", "target=editor", "module_mono_enabled=yes", "extra_suffix=executable",
             f"dev_build={dev_build}", f"scu_build={scu_build}",
             "debug_symbols=true", "separate_debug_symbols=true"],
            cwd="godot",
            description=task_desc
        )

    # Generate glue
    if "--no-glue" not in sys.argv:
        console.print("\n[bold yellow]━━━ Generating Mono Glue ━━━[/bold yellow]")

        task_desc = "Creating NuGet packages directory"
        os.makedirs("godot/bin/GodotSharp/Tools/nupkgs", exist_ok=True)
        console.print(f"[bold green]✓[/bold green] {task_desc}")

        task_desc = "Generating game UID cache"
        run_with_live_output(
            [godot_exe, "--path", "../project", "--import", "--headless"],
            cwd="godot",
            description=task_desc
        )

        task_desc = "Generating Mono glue files"
        run_with_live_output(
            [godot_exe, "--headless", "--generate-mono-glue", "./modules/mono/glue"],
            cwd="godot",
            description=task_desc
        )

        task_desc = "Building C# assemblies and NuGet packages"
        run_with_live_output(
            ["python", "./modules/mono/build_scripts/build_assemblies.py",
             "--godot-platform", godot_platform, "--godot-output-dir", "./bin",
             "--no-deprecated"],
            cwd="godot",
            description=task_desc
        )

    # Build engine
    console.print("\n[bold yellow]━━━ Building Engine ━━━[/bold yellow]")

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

    # Check early exit
    if "--no-run" in sys.argv:
        console.print("\n[bold green]✓ Build complete (skipping driver run)![/bold green]")
        return

    # Restore packages
    console.print("\n[bold yellow]━━━ Restoring .NET Packages ━━━[/bold yellow]")

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

    # Final success message
    console.print()
    console.print(Panel.fit(
        "[bold green]✓ Build Complete![/bold green]\n\n"
        "Run the project using:\n"
        "[cyan]  dotnet run --project engine[/cyan]",
        border_style="green"
    ))


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        console.print("\n[bold red]Build cancelled by user[/bold red]")
        sys.exit(1)
    except Exception as e:
        console.print(f"\n[bold red]Build failed with error:[/bold red] {e}")
        import traceback
        console.print(traceback.format_exc())
        sys.exit(1)

