# 2dog

Command-line tool and project templates for [2dog](https://2dog.dev)  –  run
Godot as a library from your own .NET entry point. The engine library itself
is the [`2dog.engine`](https://www.nuget.org/packages/2dog.engine) package.

This one package is both a dotnet tool and a `dotnet new` template package.

## Run it

One-shot (no install, .NET 10+):

```bash
dnx 2dog add         # add hosts to the Godot project here
dnx 2dog new MyGame  # a new Godot project with 2dog hosts
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

Creates the host projects **in place**  –  no file is ever moved, renamed or
deleted. The Godot project directory becomes the solution root, and host
projects are scaffolded as nested subfolders that the Godot editor ignores
(each carries a `.gdignore`):

```
MyGame/                      <- your existing Godot project (unchanged)
  project.godot
  MyGame.csproj              <- created or minimally patched
  MyGame.slnx                <- created, or an existing .sln is migrated
  MyGame.2dog/   (.gdignore) <- desktop host (your Main entry point)
  MyGame.web/    (.gdignore) <- browser (WebAssembly) host (holds TwoDogWebBoot.cs)
  MyGame.tests/  (.gdignore) <- xUnit test project
  MyGame.winforms/ (.gdignore) <- WinForms host (opt-in: --winforms; Windows-only at runtime)
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
| `2dog pack list <file.pck>` | List a `.pck`'s contents by size (no engine involved) |

Options:

| Option | Effect |
| --- | --- |
| `--desktop [folder]`, `--web [folder]`, `--tests [folder]`, `--winforms [folder]` | Add a host, optionally in a named folder (repeatable; winforms is Windows-only and never in the default set) |
| `--no-desktop`, `--no-web`, `--no-tests` | Leave a host out of the default set |
| `-n, --name <BaseName>` | Project name (`new`) or base name override |
| `-o, --output <dir>` | Directory for a new project |
| `-y, --yes`, `--non-interactive` | Do not prompt; take the flags and defaults |
| `--dry-run` | Print planned actions without changing anything |
| `--force` | Overwrite files that already exist (never deletes/moves) |
| `--no-restore` | Skip the final `dotnet restore` |
| `--verbose` | Extra output |

## Using the library directly

This package cannot be referenced from a project (it is a tool package);
reference the engine instead:

```bash
dotnet add package 2dog.engine
```
