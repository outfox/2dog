---
title: 2dog new
description: "Reference for 2dog new: create a Godot project with 2dog hosts - arguments, host flags, options, examples."
---

# `2dog new`

Creates a new Godot project with 2dog hosts. Without host flags it asks which
hosts to create. Host flags skip that wizard; a missing workload (`wasm-tools`,
`android`) can still be offered on terminals. `-y` runs unattended.

```bash
2dog new [Name] [dir] [hosts] [options]
```

| Argument | Meaning |
| --- | --- |
| `Name` | Project name, used for folders, assemblies and namespaces; letters, digits, `.` and `_` survive, `-` becomes `_`, C# keywords get a `_` prefix, an adjustment is announced |
| `dir` | Directory to create; defaults to the sanitized name |

## Hosts

Any [host flag](/dnx-2dog#host-flags), repeatable. Unattended without one:
generic, browser and tests, minus the `--no-<host>` ones.

## Options

| Option | Effect |
| --- | --- |
| `-n, --name <name>` | Project name; same as `Name` |
| `-o, --output <dir>` | Directory; same as `dir` |
| `--dry-run` | Print the plan; change nothing |
| `--force` | Overwrite scaffolded files that exist; never deletes |
| `--no-restore` | Skip the final `dotnet restore` |
| `--install-wasm-tools` | Install missing `wasm-tools` for browser hosts without an installation prompt |
| `--install-android-workload` | Install the missing `android` workload for Android hosts without an installation prompt |

Plus the [global and output options](/dnx-2dog#global-options).

`--no-restore` skips the workload offers; the install flags request
installation explicitly. `--dry-run` never checks or installs workloads.

## Examples

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then create
and run a project with a generic .NET host:

```bash
dnx 2dog new MyGame --generic
cd MyGame
dotnet run --project MyGame.2dog
```

Open `project.godot` in the Godot .NET editor to edit scenes and scripts.
See [Project Layout](/project-layout) for the generated files and
[Hosts](/hosts/) for host types.

Choose hosts interactively, or supply host flags:

```bash
2dog new MyGame                            # interactive host choice
2dog new MyGame --generic --tests          # unattended
2dog new "My Game" -o games/mine --no-web  # name adjusted to MyGame
```

For an existing project, use [`2dog add`](/cli/add).

## Use the .NET Template

The same template is available through `dotnet new`:

```bash
dotnet new install 2dog
dotnet new 2dog -n MyGame --web false
```

This creates generic .NET and xUnit hosts. Omit `--web false` to include Web;
install `wasm-tools` first.
