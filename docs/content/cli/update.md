---
title: 2dog update
description: "Reference for 2dog update: bring a project's 2dog packages to the running tool's versions - arguments, options, examples."
---

# `2dog update`

Sets the project's 2dog package versions to the running tool's, and restores.
Never downgrades; refuses a dirty git tree. What it rewrites, step by step:
[Updating a project](/doctor#updating-a-project).

```bash
2dog update [path] [options]
```

| Argument | Meaning |
| --- | --- |
| `path` | Directory containing `project.godot`; defaults to the current directory |

## Options

| Option | Effect |
| --- | --- |
| `--dry-run` | Print the plan; change nothing |
| `--no-restore` | Skip the final `dotnet restore` |
| `--allow-dirty` | Proceed although the git working tree has uncommitted changes |

Plus the [global and output options](/dnx-2dog#global-options). There is no
`--to`: pin the tool instead.

## Examples

```bash
2dog update                            # here, after committing
2dog update path/to/project --dry-run  # show what would change
dnx 2dog@:2dog-version: update         # a specific tool version
```
