---
title: 2dog update
description: "Reference for 2dog update: bring a project's 2dog packages to the running tool's versions - arguments, options, examples."
---

# `2dog update`

Sets the project's 2dog package versions to the running tool's, and restores
after project changes. Never downgrades. When Git can check the project, it
refuses uncommitted tracked-file changes unless you pass `--allow-dirty`.

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
| `--allow-dirty` | Proceed despite uncommitted tracked-file changes |
| `--install-wasm-tools` | Install missing `wasm-tools` for browser hosts |
| `--update-workloads` | Install missing `wasm-tools`, or run `dotnet workload update` for the project's SDK |

Plus the [global and output options](/dnx-2dog#global-options). There is no
`--to`: pin the tool instead.

For browser hosts, terminals offer installation if `wasm-tools` is missing,
or a separate update if it is installed. `dotnet workload update` checks for
and applies updates to **all installed workloads** for the selected SDK.
`--no-restore` skips these offers; the explicit flags still apply.
`--dry-run` never checks or changes workloads.

## Examples

```bash
2dog update                            # here, after committing
2dog update path/to/project --dry-run  # show what would change
dnx 2dog@:2dog-version: update         # a specific tool version
```
