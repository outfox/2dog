# 2dog

Command-line tool and project templates for [2dog](https://2dog.dev)  –  run
Godot as a library from your own .NET entry point. It scaffolds host projects,
checks and repairs them (`2dog doctor`), and updates them (`2dog update`). The
engine library itself is the [`2dog.engine`](https://www.nuget.org/packages/2dog.engine)
package.

This one package is both a dotnet tool and a `dotnet new` template package.

## Run it

One-shot (no install, .NET 10+):

```bash
dnx 2dog add         # add hosts to the Godot project here
dnx 2dog new MyGame  # a new Godot project with 2dog hosts
dnx 2dog doctor      # check the project and this machine, fix what it can
dnx 2dog update      # bring the 2dog packages to this tool's versions
```

Or install the `2dog` command globally:

```bash
dotnet tool install -g 2dog
2dog add
```

With no host options the tool prompts: a checkbox list of hosts, editable
folder names, the plan, and a confirmation. Naming any host option (or passing
`--yes`) runs it unattended instead. The `dotnet new` template produces the
same output: `dotnet new install 2dog && dotnet new 2dog -n MyGame`.

## What it does

Creates the host projects **in place**: it creates files and edits `*.csproj`,
`project.godot`, the solution and `Directory.Build.props`; no file is ever
moved, renamed or deleted (two announced opt-ins aside: the `.sln` to `.slnx`
migration and `--rename`). The Godot project directory becomes the solution
root, and host projects are scaffolded as nested subfolders that the Godot
editor ignores (each carries a `.gdignore`):

```
MyGame/                      <- your existing Godot project (unchanged)
  project.godot
  MyGame.csproj              <- created or minimally patched
  MyGame.slnx                <- created, or an existing .sln is migrated
  Directory.Build.props      <- the package versions every host references
  MyGame.2dog/   (.gdignore) <- desktop host (your Main entry point)
  MyGame.web/    (.gdignore) <- browser (WebAssembly) host (holds TwoDogWebBoot.cs)
  MyGame.webxr/  (.gdignore) <- WebXR browser host (opt-in: --webxr; page ships the WebXR Layers polyfill)
  MyGame.tests/  (.gdignore) <- xUnit test project
  MyGame.winforms/ (.gdignore) <- WinForms host (opt-in: --winforms; Windows-only at runtime)
  MyGame.winui/  (.gdignore) <- WinUI 3 host (opt-in: --winui; Windows-only, builds only on Windows)
  MyGame.avalonia/ (.gdignore) <- Avalonia host (opt-in: --avalonia; cross-platform GUI)
  MyGame.blazor/ (.gdignore) <- Blazor Web App host (opt-in: --blazor; server + Client/ WebAssembly page)
```

Run it again whenever you want another host  –  hosts that exist are recognized
and left alone, and a kind you already have is added a second time under a
free folder name (`2dog add --desktop MyGame.editor`).

Commands:

| Command | Effect |
| --- | --- |
| `2dog` | Print version info and usage |
| `2dog new [Name] [dir]` | Create a new Godot project with 2dog hosts |
| `2dog add [path]` | Add hosts to an existing Godot project |
| `2dog convert [path]` | Alias of `add`, for projects that have no hosts yet |
| `2dog doctor [path]` | Check the project and this machine; `--fix` applies the safe fixes; `--build` / `--log` explain build failures |
| `2dog update [path]` | Update the project's 2dog packages to this tool's versions (never downgrades) |
| `2dog pack list <file.pck>` | List a `.pck`'s contents by size (no engine involved) |
| `2dog version` | Print the tool and package versions |
| `2dog help [verb]` | Usage, or the help for one verb |

Options:

| Option | Effect |
| --- | --- |
| `--desktop [folder]`, `--web [folder]`, `--webxr [folder]`, `--tests [folder]`, `--winforms [folder]`, `--winui [folder]`, `--avalonia [folder]`, `--blazor [folder]` | Add a host, optionally in a named folder (repeatable; webxr, winforms, winui, avalonia, and blazor are opt-in and never in the default set) |
| `--no-desktop`, `--no-web`, `--no-tests` | Leave a host out of the default set |
| `-n, --name <BaseName>` | Project name (`new`) or base name override |
| `--rename <NewName>` | Fix a .NET project name that contains spaces (`add`/`convert`, before any hosts exist) |
| `-o, --output <dir>` | Directory for a new project |
| `-y, --yes`, `--non-interactive` | Do not prompt; take the flags and defaults |
| `--dry-run` | Print planned actions without changing anything |
| `--force` | Overwrite files that already exist (never deletes/moves) |
| `--no-restore` | Skip the final `dotnet restore` |
| `--allow-dirty` | `update`: proceed with uncommitted git changes |
| `--fix`, `--fix-all`, `--build [target]`, `-c, --configuration <Cfg>`, `--log <file>`, `--ignore <id>`, `--strict`, `--offline`, `--list-checks` | `doctor` options; see `2dog doctor --help` |
| `--json`, `-q, --quiet`, `--plain`, `--no-color`, `--accessible`, `-v, --verbose` | Output modes: machine-readable, terse, no styling, screen-reader friendly, extra detail |
| `-h, --help`, `--version` | Help (per verb after a verb), versions |

Exit codes: `0` ok, `1` usage error, `2` tool error, `3` doctor findings remain,
`130` cancelled. The report goes to stdout, diagnostics to stderr; nothing
prompts in a pipe or in CI. Full reference: https://2dog.dev/dnx-2dog

## Using the library directly

This package cannot be referenced from a project (it is a tool package);
reference the engine instead:

```bash
dotnet add package 2dog.engine
```
