# 2dog

Command-line tool and project templates for [2dog](https://2dog.dev) – run
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
`--yes`) skips that wizard. On terminals, a missing workload (`wasm-tools`,
`android`) can still be offered for newly selected hosts before restore; `--yes` skips the offer. Use
`--install-wasm-tools` or `--install-android-workload` for explicit installation. The `dotnet new` template produces the
same output: `dotnet new install 2dog && dotnet new 2dog -n MyGame`.

Browser and Android hosts are excluded from ordinary solution builds. Install their
workloads when building or publishing those hosts. Package updates and desktop/test
builds do not require them. Doctor only requires them for its selected `--build`
target, or installs them when explicitly requested with an install flag.
Failed restores return exit code `2`, preserve the created files, and explain
recognized failures; fix the cause and run `dotnet restore` again.

## What it does

Creates the host projects **in place**: it creates files and edits `*.csproj`,
`project.godot`, the solution and `Directory.Build.props`; no file is ever
moved, renamed or deleted (two announced opt-ins aside: the `.sln` to `.slnx`
migration and `--rename`). Solution migration first saves a `.sln.old` backup,
using `.sln.old.1`, `.sln.old.2`, and so on if needed, and removes the `.sln`
only after conversion succeeds. Existing backups are kept.
The Godot project directory becomes the solution
root, and host projects are scaffolded as nested subfolders that the Godot
editor ignores (each carries a `.gdignore`):

```
MyGame/                      <- your existing Godot project (unchanged)
  project.godot
  MyGame.csproj              <- created or minimally patched
  MyGame.slnx                <- created, or an existing .sln is migrated
  Directory.Build.props      <- the package versions every host references
  MyGame.2dog/   (.gdignore) <- generic .NET host (your Main entry point)
  MyGame.web/    (.gdignore) <- browser (WebAssembly) host (holds TwoDogWebBoot.cs)
  MyGame.webxr/  (.gdignore) <- WebXR browser host (opt-in: --webxr; page ships the WebXR Layers polyfill)
  MyGame.xunit/  (.gdignore) <- xUnit test project
  MyGame.nunit/  (.gdignore) <- NUnit test project (opt-in: --nunit)
  MyGame.winforms/ (.gdignore) <- WinForms host (opt-in: --winforms; Windows-only at runtime)
  MyGame.winui/  (.gdignore) <- WinUI 3 host (opt-in: --winui; Windows-only, builds only on Windows)
  MyGame.avalonia/ (.gdignore) <- Avalonia host (opt-in: --avalonia; cross-platform GUI)
  MyGame.blazor/ (.gdignore) <- Blazor Web App host (opt-in: --blazor; server + Client/ WebAssembly page)
  MyGame.android/ (.gdignore) <- Android host (opt-in: --android; dotnet publish makes an APK)
```

Run it again whenever you want another host – hosts that exist are recognized
and left alone, and a kind you already have is added a second time under a
free folder name (`2dog add --generic MyGame.editor`).

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
| `--generic [folder]`, `--web [folder]`, `--webxr [folder]`, `--tests [folder]`, `--nunit [folder]`, `--winforms [folder]`, `--winui [folder]`, `--avalonia [folder]`, `--blazor [folder]`, `--android [folder]` | Add a host, optionally in a named folder (repeatable; nunit, webxr, winforms, winui, avalonia, blazor, and android are opt-in and never in the default set) |
| `--no-generic`, `--no-web`, `--no-tests`, `--no-nunit` | Leave a host out of the default set |
| `-n, --name <BaseName>` | Project name (`new`) or base name override |
| `--rename <NewName>` | Fix a .NET project name that contains spaces (`add`/`convert`, before any hosts exist) |
| `-o, --output <dir>` | Directory for a new project |
| `-y, --yes`, `--non-interactive` | Do not prompt; take the flags and defaults |
| `--dry-run` | Print planned actions without changing anything |
| `--force` | Overwrite files that already exist (never deletes/moves) |
| `--no-restore` | Skip the final `dotnet restore` |
| `--install-wasm-tools` | `new`, `add`, `doctor`, `update`: install missing `wasm-tools` for browser hosts |
| `--install-android-workload` | `new`, `add`, `doctor`, `update`: install the missing `android` workload for Android hosts |
| `--update-workloads` | `update`: install missing workloads the hosts need, then update all installed workloads for the project's SDK |
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
