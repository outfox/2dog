---
title: dnx 2dog
description: "Reference for the 2dog command-line tool - installation, commands, host flags, options, output modes, exit codes, and version pinning."
---

# `dotnet tool execute 2dog`

`2dog` is the command-line tool that scaffolds .NET host projects around a
Godot project, keeps them healthy, and updates them: your project directory
becomes the solution root, and desktop, browser, WebXR, Blazor, test, WinForms,
WinUI 3, and Avalonia hosts live in nested folders Godot ignores. It creates
files and edits `*.csproj`, `project.godot`, the solution and
`Directory.Build.props` in place; it never moves, renames, or deletes existing
files, with two announced exceptions you opt into: the `.sln` → `.slnx`
migration, and the [`--rename` fix](/add#project-names-with-spaces) for project
names containing spaces.

The tool and the matching `dotnet new` template ship together in the
[`2dog` NuGet package](https://www.nuget.org/packages/2dog/).

## Installation

No installation needed - `dnx` (part of the .NET 10 SDK) downloads and runs
the latest version:

```bash
dnx 2dog add
```

Or install it as a global tool:

```bash
dotnet tool install -g 2dog
2dog add
```

Run without a verb, `2dog` prints its version info and usage; `2dog <verb>
--help` prints the help for one verb. `2dog new`, `2dog add` and `2dog doctor`
are interactive on a terminal when no deciding flags are given: they ask which
hosts to add (or which fixes to apply), show their plan, and wait for
confirmation. Any host flag, `--yes`, or a pipe turns the prompts off for
scripts and CI.

## Commands

| Command | Effect |
| --- | --- |
| `2dog` | Print version info and usage |
| `2dog new [Name] [dir]` | Create a new Godot project with 2dog hosts |
| `2dog add [path]` | Add hosts to an existing Godot project |
| `2dog convert [path]` | Alias of `add` for a project with no hosts yet |
| `2dog doctor [path]` | [Check the project and this machine](/doctor), fix what can be fixed, explain build failures |
| `2dog update [path]` | [Update the project's 2dog packages](/doctor#updating-a-project) to this tool's versions |
| `2dog pack list <file.pck>` | List a `.pck`'s contents by size (no engine involved) |
| `2dog version` | Print the tool and referenced package versions |
| `2dog help [verb]` | Show usage, or the help for one verb |

## Inspecting packs

`2dog pack list` parses a pack's directory straight from the file - no engine,
no project - and prints every entry sorted by size. Useful to answer "why is my
pck 99 MiB?" or to confirm an asset (say, a fallback font) actually made it
into a [web publish](/hosts/web):

```bash
2dog pack list MyGame.web/AppBundle/godot.pck
```

## Host flags

Each flag adds one host; the folder name is optional. Repeat a flag to add a
second host of the same kind.

| Flag | Effect |
| --- | --- |
| `--desktop [folder]` | [Generic desktop host](/hosts/generic) with your own `Main()` |
| `--web [folder]` | [Browser (WebAssembly) host](/hosts/web) |
| `--webxr [folder]` | [WebXR host](/hosts/webxr) (browser host with the WebXR Layers polyfill; never part of the default set) |
| `--tests [folder]` | [xUnit test project](/hosts/xunit) |
| `--winforms [folder]` | [WinForms host](/hosts/winforms) (Windows-only; never part of the default set) |
| `--winui [folder]` | [WinUI 3 host](/hosts/winui) (Windows-only, builds only on Windows; never part of the default set) |
| `--avalonia [folder]` | [Avalonia host](/hosts/avalonia) (cross-platform GUI; never part of the default set) |
| `--blazor [folder]` | [Blazor Web App host](/hosts/blazor) (server + WebAssembly client page embedding the game; never part of the default set) |
| `--no-desktop`, `--no-web`, `--no-tests` | Leave a host out of the default set (every kind has a `--no-<host>` form; the opt-in kinds are never in the set, so theirs change nothing) |

## Options

| Option | Effect |
| --- | --- |
| `-n, --name <name>` | Project name (`new`) or base-name override. Names are reduced to letters, digits, `.`, `_` and `-`; an adjustment is announced |
| `--rename <NewName>` | Fix a project whose .NET name contains spaces ([breaks publish](/add#project-names-with-spaces)): renames the csproj, sets `assembly_name`, repoints the solution. `add`/`convert` only, before any hosts exist |
| `-o, --output <dir>` | Directory for a new project |
| `-y, --yes` | Use the flags and defaults without prompting (also `--non-interactive`, `--no-input`) |
| `--dry-run` | Print planned actions without changing anything (`new`, `add`, `update`) |
| `--force` | Overwrite existing scaffolded files; never delete or move files |
| `--no-restore` | Skip the final `dotnet restore` |
| `--allow-dirty` | `update`: proceed although the git working tree has uncommitted changes |
| `--fix`, `--fix-all` | `doctor`: apply the safe fixes, or the announced ones too |
| `--build [target]`, `-c, --configuration <Cfg>` | `doctor`: build the solution (or a host folder or project) and explain known failures |
| `--log <file>` | `doctor`: only explain an existing build, restore or runtime log (`-` reads stdin) |
| `--ignore <id>`, `--strict`, `--offline`, `--list-checks` | `doctor`: drop a finding, make warnings count, skip nuget.org, list every check |
| `--version` | Same as the `version` command |
| `-h, --help` | Show help; after a verb, the help for that verb |

Options take their value attached or separate: `--name Foo`, `--name=Foo`.
The global ones (`-y`, `--help`, `--version` and the output options below) may
also come before the verb: `2dog --json add`.
A `--` ends option parsing, so a path starting with a dash still works.
Mistyped options and verbs get a suggestion (`unknown option '--dekstop' (did
you mean --desktop?)`).

## Output and environment

| Option | Effect |
| --- | --- |
| `--json` | Machine-readable output: exactly one JSON document on stdout and nothing on stderr, also when the run fails (`"ok": false`). Implies `--yes` |
| `-q, --quiet` | Results and problems only: no header, plan, progress lines or next steps |
| `--plain` | No colour, no cursor movement, ASCII markers instead of `✓`/`✗` (also `TERM=dumb`) |
| `--no-color` | No colour, cursor movement stays (also the `NO_COLOR` environment variable) |
| `--accessible` | Screen-reader friendly prompts: numbered yes/no questions instead of lists, no spinners (also `TWODOG_ACCESSIBLE=1`) |
| `-v, --verbose` | Extra detail on stderr: every subprocess command line and its output, stack traces |

The report goes to **stdout**, diagnostics (`error:`, `warning:`, `note:`,
`hint:`, `verbose:`, spinners) to **stderr**, so `2dog add --json | jq .` and
`2dog doctor 2>errors.log` both work. When stdout is not a terminal, or a CI
environment is detected (`CI`, `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`,
`TEAMCITY_VERSION`, `BUILD_NUMBER`), nothing prompts, nothing animates, and the
markers fall back to ASCII. Prompts wanted but impossible are replaced by the
defaults with a `note:` saying so. `CLICOLOR_FORCE` or `FORCE_COLOR` keeps
colour in a pipe.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success, including `--dry-run`, "Nothing to do", and a declined plan |
| `1` | Usage error: unknown option or verb, missing value, `--pin`, wrong positional count. The error names the verb's `--help` |
| `2` | Tool error: project state, I/O, invalid XML, a failed subprocess, a plan that stopped half-way (the report says which steps stand), an unexpected exception (`--verbose` adds the stack trace) |
| `3` | `doctor`: findings remain after the fixes (errors; warnings too under `--strict`), or the `--build` failed |
| `130` | Cancelled with Ctrl+C |

## Versions and pinning

`2dog version` prints the tool version and every package a scaffold
references:

```text
2dog :2dog-version:  https://2dog.dev

tool + packages  :2dog-version: ✅  2dog, 2dog.engine, 2dog.avalonia, 2dog.blazor, 2dog.xunit
native binaries  :natives-version: ✅  2dog.win-x64, 2dog.linux-x64, 2dog.osx-arm64, 2dog.browser-wasm, 2dog.tools
Godot SDK        :godot-version: ✅  Godot.NET.Sdk, GodotSharp
```

Each row is checked against nuget.org: ✅ (`ok` when redirected) means the
version is the latest stable release, 🔄 (`new`) means a newer stable release
exists. When nuget.org cannot be reached the marks are simply left out.

Scaffolded projects keep every package version in the root
`Directory.Build.props` (`TwoDogVersion`, `TwoDogNativesVersion`,
`TwoDogGodotVersion` and the companion versions); host csprojs reference
`$(TwoDogVersion)` and friends. [`2dog update`](/doctor#updating-a-project)
rewrites that one block.

::: warning --version under dnx
`dnx 2dog --version` never reaches the tool: `--version <VERSION>` is `dnx`'s
own option and selects which version of the `2dog` package to download and
run. Use `dnx 2dog version` to print versions, and `dnx 2dog@:2dog-version:`
(or the `--version` option) to run a specific tool version.
:::

## Learn more

- [Adding 2dog to existing projects](/add) - workflow, what gets generated, and safety guarantees
- [Creating a new project](/templates) - the generated layout, and the `dotnet new 2dog` template
- [Doctor and update](/doctor) - checks, fixes, build-failure explanations, upgrading
- [Troubleshooting](/troubleshooting) - exit codes, CI behaviour, restore and build failures
