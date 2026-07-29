---
title: dnx 2dog
description: "Reference for the 2dog command-line tool - installation, commands, host flags, options, and version pinning."
---

# dnx 2dog

`2dog` is the command-line tool that scaffolds .NET host projects around a
Godot project: your project directory becomes the solution root, and desktop,
browser, test, and WinForms hosts live in nested folders Godot ignores. It
never moves, renames, or deletes existing files.

The tool and the matching `dotnet new` template ship together in the
[`2dog` NuGet package](https://www.nuget.org/packages/2dog/).

## Installation

No installation needed - `dnx` (part of the .NET 10 SDK) downloads and runs
the latest version:

```bash
dnx 2dog
```

Or install it as a global tool:

```bash
dotnet tool install -g 2dog
2dog
```

Run without any host flags, the tool is interactive: it detects whether the
directory holds a Godot project, asks which hosts to add, shows its plan, and
waits for confirmation. Any host flag, `--yes`, or `--non-interactive` turns
the prompts off for scripts and CI.

## Commands

| Command | Effect |
| --- | --- |
| `2dog` | Add hosts to the Godot project here, or create a project if there is none |
| `2dog new [Name] [dir]` | Create a new Godot project with 2dog hosts |
| `2dog add [path]` | Add hosts to an existing Godot project |
| `2dog convert [path]` | Alias of `add` for a project with no hosts yet |
| `2dog version` | Print the tool version and the versions of every package it references |
| `2dog help` | Show usage |

## Host flags

Each flag adds one host; the folder name is optional. Repeat a flag to add a
second host of the same kind.

| Flag | Effect |
| --- | --- |
| `--desktop [folder]` | [Generic desktop host](/hosts/generic) with your own `Main()` |
| `--web [folder]` | [Browser (WebAssembly) host](/hosts/web) |
| `--tests [folder]` | [xUnit test project](/hosts/xunit) |
| `--winforms [folder]` | [WinForms host](/hosts/winforms) (Windows-only; never part of the default set) |
| `--no-desktop`, `--no-web`, `--no-tests` | Leave a host out of the default set |

## Options

| Option | Effect |
| --- | --- |
| `-n, --name <BaseName>` | Project name (`new`) or base-name override |
| `-o, --output <dir>` | Directory for a new project |
| `-y, --yes` | Use the flags and defaults without prompting |
| `--non-interactive` | Same as `--yes` |
| `--dry-run` | Print planned actions without changing anything |
| `--force` | Overwrite existing scaffolded files; never delete or move files |
| `--no-restore` | Skip the final `dotnet restore` |
| `--verbose` | Show extra output |
| `--version` | Same as the `version` command |

## Versions and pinning

`2dog version` prints the tool version and every package a scaffold
references:

```text
2dog :2dog-version: - https://2dog.dev

tool + packages  :2dog-version:    2dog, 2dog.engine, 2dog.xunit
native binaries  :natives-version:    2dog.win-x64, 2dog.linux-x64, 2dog.osx-arm64, 2dog.browser-wasm, 2dog.tools
Godot SDK        :godot-version:       Godot.NET.Sdk, GodotSharp
```

::: warning --version under dnx
`dnx 2dog --version` never reaches the tool: `--version <VERSION>` is `dnx`'s
own option and selects which version of the `2dog` package to download and
run. Use `dnx 2dog version` to print versions, and `dnx 2dog@:2dog-version:`
(or the `--version` option) to run a specific tool version.
:::

## Learn more

- [Adding 2dog to existing projects](/add) - workflow, what gets generated, and safety guarantees
- [Creating a new project](/templates) - the generated layout, and the `dotnet new 2dog` template
