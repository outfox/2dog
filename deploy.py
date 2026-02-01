#!/usr/bin/env python3
"""Push NuGet packages from packages/ to nuget.org."""

import os
import subprocess
import sys
from pathlib import Path

from rich.console import Console
from rich.panel import Panel
from rich.table import Table

console = Console()
PACKAGES_DIR = Path(__file__).parent / "packages"


def get_api_key() -> str:
    key = os.environ.get("NUGET_API_KEY", "")
    if not key:
        console.print("[bold red]NUGET_API_KEY environment variable is not set.[/bold red]")
        console.print("Get an API key from https://www.nuget.org/account/apikeys")
        console.print("\nUsage:")
        console.print("  [cyan]NUGET_API_KEY=your-key uv run poe deploy[/cyan]")
        sys.exit(1)
    return key


def find_packages() -> list[Path]:
    if not PACKAGES_DIR.exists():
        console.print(f"[bold red]Packages directory not found:[/bold red] {PACKAGES_DIR}")
        console.print("Run [cyan]uv run poe build[/cyan] first.")
        sys.exit(1)

    packages = sorted(PACKAGES_DIR.glob("*.nupkg"))
    # Exclude symbol packages — nuget.org accepts them alongside the main .nupkg automatically
    packages = [p for p in packages if not p.name.endswith(".snupkg")]

    if not packages:
        console.print("[bold red]No .nupkg files found in packages/[/bold red]")
        console.print("Run [cyan]uv run poe build[/cyan] first.")
        sys.exit(1)

    return packages


def push_package(package: Path, api_key: str) -> bool:
    result = subprocess.run(
        [
            "dotnet", "nuget", "push",
            str(package),
            "--source", "https://api.nuget.org/v3/index.json",
            "--api-key", api_key,
            "--skip-duplicate",
        ],
        capture_output=True,
        text=True,
    )

    if result.returncode != 0:
        # --skip-duplicate exits 0 on "already exists", so a non-zero exit is a real error
        console.print(f"  [bold red]✗[/bold red] {package.name}")
        console.print(f"    [dim red]{result.stderr.strip() or result.stdout.strip()}[/dim red]")
        return False

    if "already exists" in result.stdout.lower() or "already exists" in result.stderr.lower():
        console.print(f"  [dim]⊘[/dim] {package.name} [dim](already exists)[/dim]")
    else:
        console.print(f"  [bold green]✓[/bold green] {package.name}")

    return True


def main():
    api_key = get_api_key()
    packages = find_packages()

    # Show what we're about to push
    table = Table(title="Packages to Deploy", show_header=True, header_style="bold magenta")
    table.add_column("Package", style="cyan")
    table.add_column("Size", style="green", justify="right")

    for pkg in packages:
        size_kb = pkg.stat().st_size / 1024
        if size_kb >= 1024:
            size_str = f"{size_kb / 1024:.1f} MB"
        else:
            size_str = f"{size_kb:.0f} KB"
        table.add_row(pkg.name, size_str)

    console.print(table)
    console.print()

    console.print(
        Panel.fit(
            f"[bold white]Pushing {len(packages)} package(s) to nuget.org[/bold white]",
            border_style="cyan",
        )
    )

    failed = []
    for pkg in packages:
        if not push_package(pkg, api_key):
            failed.append(pkg)

    console.print()

    if failed:
        console.print(
            Panel.fit(
                f"[bold red]✗ {len(failed)} package(s) failed to push[/bold red]",
                border_style="red",
            )
        )
        sys.exit(1)
    else:
        console.print(
            Panel.fit(
                f"[bold green]✓ All {len(packages)} package(s) deployed successfully[/bold green]",
                border_style="green",
            )
        )


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        console.print("\n[bold red]Deploy cancelled by user[/bold red]")
        sys.exit(1)
