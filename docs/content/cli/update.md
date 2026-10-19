---
title: 2dog update
description: "Reference for 2dog update: bring a project's 2dog packages to the running tool's versions - arguments, options, examples."
tagline: "Learn how to bring your project's 2dog packages up to date."
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
| `--install-android-workload` | Install the missing `android` workload for Android hosts |
| `--update-workloads` | Install missing workloads the hosts need, then run `dotnet workload update` for the project's SDK |

Plus the [global and output options](/dnx-2dog#global-options). There is no
`--to`: pin the tool instead.

Package updates need no workloads or Android SDK. For browser or Android
hosts, terminals can offer to update workloads;
[`dotnet workload update`](https://learn.microsoft.com/dotnet/core/tools/dotnet-workload-update)
updates **all installed workloads** for the SDK. `--no-restore` skips the
offer, and `--dry-run` never touches workloads.

## Examples

```bash
2dog update                            # here, after committing
2dog update path/to/project --dry-run  # show what would change
dnx 2dog@:2dog-version: update         # a specific tool version
```
