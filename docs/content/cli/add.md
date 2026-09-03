---
title: 2dog add
description: "Reference for 2dog add: add .NET hosts to an existing Godot project in place - arguments, host flags, options, examples."
---

# `2dog add`

Adds hosts to an existing Godot project, in place. Run it again to add more
hosts, including a second host of the same kind. Without host flags it asks
interactively; any host flag or `-y` runs unattended. `2dog convert` is an
alias, for projects that have no hosts yet.

```bash
2dog add [path] [hosts] [options]
```

| Argument | Meaning |
| --- | --- |
| `path` | Directory containing `project.godot`; defaults to the current directory |

## Hosts

Any [host flag](/dnx-2dog#host-flags), repeatable. Unattended without one:
desktop, browser and tests, minus the `--no-<host>` ones. Existing hosts are
recognized and skipped.

## Options

| Option | Effect |
| --- | --- |
| `-n, --name <name>` | Base name override for the scaffolded files; letters, digits, `.`, `_` and `-` survive |
| `--rename <NewName>` | Rename a .NET project name that [contains spaces](/add#project-names-with-spaces), then scaffold; only before any hosts exist |
| `--dry-run` | Print the plan; change nothing |
| `--force` | Overwrite scaffolded files that exist; never deletes |
| `--no-restore` | Skip the final `dotnet restore` |

Plus the [global and output options](/dnx-2dog#global-options).

## Examples

```bash
2dog add                           # interactive, here
2dog add --desktop MyGame.editor   # a second desktop host, named
2dog add path/to/project --no-web
2dog add --rename MyGame           # fix a spaced .NET name first
```

Workflow, generated files and safety guarantees:
[Adding 2dog to Existing Projects](/add).
