---
title: 2dog new
description: "Reference for 2dog new: create a Godot project with 2dog hosts - arguments, host flags, options, examples."
---

# `2dog new`

Creates a new Godot project with 2dog hosts. Without host flags it asks which
hosts to create; any host flag or `-y` runs unattended.

```bash
2dog new [Name] [dir] [hosts] [options]
```

| Argument | Meaning |
| --- | --- |
| `Name` | Project name; letters, digits, `.`, `_` and `-` survive, an adjustment is announced |
| `dir` | Directory to create; defaults to the sanitized name |

## Hosts

Any [host flag](/dnx-2dog#host-flags), repeatable. Unattended without one:
desktop, browser and tests, minus the `--no-<host>` ones.

## Options

| Option | Effect |
| --- | --- |
| `-n, --name <name>` | Project name; same as `Name` |
| `-o, --output <dir>` | Directory; same as `dir` |
| `--dry-run` | Print the plan; change nothing |
| `--force` | Overwrite scaffolded files that exist; never deletes |
| `--no-restore` | Skip the final `dotnet restore` |

Plus the [global and output options](/dnx-2dog#global-options).

## Examples

```bash
2dog new MyGame                            # interactive host choice
2dog new MyGame --desktop --tests          # unattended
2dog new "My Game" -o games/mine --no-web  # name adjusted to MyGame
```

The generated layout and the `dotnet new 2dog` template:
[Creating a New Project](/templates).
