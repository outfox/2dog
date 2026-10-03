---
title: 2dog add
description: "Reference for 2dog add: add .NET hosts to an existing Godot project in place - arguments, host flags, options, examples."
---

# `2dog add`

Adds hosts to an existing Godot project, in place. Run it again to add more
hosts, including a second host of the same kind. 

Without host flags it asks which hosts to add. Host flags skip that wizard;
on terminals, a missing workload (`wasm-tools`, `android`) can still be offered.
`-y` runs unattended.

```bash
2dog add [path] [hosts] [options]
```

| Argument | Meaning |
| --- | --- |
| `path` | Directory containing `project.godot`; defaults to the current directory |

## Hosts

Any [host flag](/dnx-2dog#host-flags), repeatable. Unattended without one:
generic, browser and tests, minus the `--no-<host>` ones. Existing hosts are
recognized and skipped.

The new hosts are built for the running tool's versions, so the game project's
`Godot.NET.Sdk` and an existing 2dog version block in `Directory.Build.props`
are raised to them in the same run (never lowered), as `2dog update` would. A
move across Godot lines is called out: install the matching editor and open
the project once.

## Options

| Option | Effect |
| --- | --- |
| `-n, --name <name>` | Base name override for the scaffolded files; letters, digits, `.` and `_` survive, `-` becomes `_`, C# keywords get a `_` prefix |
| `--rename <NewName>` | Rename a .NET project name that [contains spaces](/known-issues/spaced-project-names), then scaffold; only before any hosts exist |
| `--dry-run` | Print the plan; change nothing |
| `--force` | Overwrite scaffolded files that exist; never deletes |
| `--no-restore` | Skip the final `dotnet restore` |
| `--install-wasm-tools` | Install missing `wasm-tools` for browser hosts without an installation prompt |
| `--install-android-workload` | Install the missing `android` workload for Android hosts without an installation prompt |

Plus the [global and output options](/dnx-2dog#global-options).

Before restore, browser hosts get a `wasm-tools` installation offer on terminals,
and Android hosts an `android` one. `--no-restore` skips the offers; the install
flags request installation explicitly, including in CI. `--dry-run` never checks or installs workloads.

## Examples

```bash
2dog add                           # interactive, here
2dog add --generic MyGame.editor   # a second generic host, named
2dog add --android                 # an Android host
2dog add path/to/project --no-web
2dog add --rename MyGame           # fix a spaced .NET name first
```

For a quick start, see [Adding 2dog to Existing Projects](/add).
For generated files, see [Project Layout](/project-layout).
