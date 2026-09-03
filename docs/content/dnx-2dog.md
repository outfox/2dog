---
title: dnx 2dog
description: "Reference for the 2dog command-line tool: installation, commands, host flags, global and output options, exit codes, version pinning."
---

# `dotnet tool execute 2dog`

`2dog` scaffolds .NET host projects around a Godot project, keeps them healthy,
and updates them. The project directory becomes the solution root; hosts live
in nested folders Godot ignores. It creates files and edits `*.csproj`,
`project.godot`, the solution and `Directory.Build.props` in place. Nothing is
moved, renamed or deleted, except two announced opt-ins: the `.sln` → `.slnx`
migration and the [`--rename` fix](/add#project-names-with-spaces).

The tool and the matching `dotnet new` template ship together in the
[`2dog` NuGet package](https://www.nuget.org/packages/2dog/).

## Installation

```bash
dnx 2dog add                  # run latest from nuget
dotnet tool install -g 2dog   # or install it globally
```

## Commands

| Command | Use it to |
| --- | --- |
| [`2dog new`](/cli/new) | Create a Godot project with 2dog hosts |
| [`2dog add`](/cli/add) | Add hosts to an existing Godot project (`2dog convert` is an alias) |
| [`2dog doctor`](/cli/doctor) | Check the project and this machine, fix what can be fixed, explain build failures |
| [`2dog update`](/cli/update) | Bring the project's 2dog packages to this tool's versions |
| [`2dog pack`](/cli/pack) | List a `.pck`'s contents |
| [`2dog version`](/cli/version) | Print the tool and package versions |
| [`2dog help`](/cli/help) | Show the usage, or the help for one verb |

`2dog` alone prints the version info and the usage. `new`, `add` and `doctor`
prompt on a terminal when no deciding flag is given; any host flag, `--yes` or
a pipe turns the prompts off.

## Host flags

`new` and `add` take one flag per host. The folder is optional; a repeated
flag adds a second host of the same kind.

| Flag | Host |
| --- | --- |
| `--desktop [folder]` | [Generic desktop host](/hosts/generic) with your own `Main()` |
| `--web [folder]` | [Browser (WebAssembly) host](/hosts/web) |
| `--webxr [folder]` | [WebXR host](/hosts/webxr); opt-in |
| `--tests [folder]` | [xUnit test project](/hosts/xunit) |
| `--winforms [folder]` | [WinForms host](/hosts/winforms); Windows-only, opt-in |
| `--winui [folder]` | [WinUI 3 host](/hosts/winui); Windows-only, builds only on Windows, opt-in |
| `--avalonia [folder]` | [Avalonia host](/hosts/avalonia); opt-in |
| `--blazor [folder]` | [Blazor Web App host](/hosts/blazor); opt-in, needs wasm-tools |
| `--no-desktop`, `--no-web`, `--no-tests` | Leave a host out of the default set (every kind has a `--no-<host>` form; opt-in kinds are never in the set) |

## Global options

Valid under every verb, also before it (`2dog --json add`).

| Option | Effect |
| --- | --- |
| `-y, --yes` | Do not prompt; take the flags and defaults (also `--non-interactive`, `--no-input`) |
| `-h, --help` | Show help; after a verb, the help for that verb |
| `--version` | Same as [`2dog version`](/cli/version); see [under dnx](#under-dnx) |

Values attach or follow: `--name Foo`, `--name=Foo`. `--` ends option parsing.
Mistyped options and verbs get a suggestion.

## Output

| Option | Effect |
| --- | --- |
| `--json` | One JSON document on stdout, nothing on stderr, also on failure (`"ok": false`); implies `--yes` |
| `-q, --quiet` | Results and problems only: no header, plan, progress or next steps |
| `--plain` | No colour, no cursor movement, ASCII markers (also `TERM=dumb`) |
| `--no-color` | No colour; cursor movement stays (also `NO_COLOR`) |
| `--accessible` | Numbered yes/no questions instead of lists, no spinners (also `TWODOG_ACCESSIBLE=1`) |
| `-v, --verbose` | Extra detail on stderr: subprocess command lines and output, stack traces |

The report goes to stdout, diagnostics (`error:`, `warning:`, `note:`, `hint:`,
`verbose:`, spinners) to stderr: `2dog add --json | jq .` and
`2dog doctor 2>errors.log` both work. In a pipe or a CI environment (`CI`,
`GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`, `TEAMCITY_VERSION`, `BUILD_NUMBER`)
nothing prompts or animates and the markers are ASCII; a wanted prompt falls
back to the default with a `note:`. `CLICOLOR_FORCE` or `FORCE_COLOR` keeps
colour in a pipe.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success, including `--dry-run`, "Nothing to do", and a declined plan |
| `1` | Usage error: unknown option or verb, missing value, wrong positional count; the error names the verb's `--help` |
| `2` | Tool error: project state, I/O, invalid XML, a failed subprocess, a plan that stopped half-way (the report says which steps stand), an unexpected exception (`--verbose` adds the stack trace) |
| `3` | `doctor`: findings remain after the fixes (errors; warnings too under `--strict`), or the `--build` failed |
| `130` | Cancelled with Ctrl+C |

## Versions

Scaffolded projects keep every package version in the root
`Directory.Build.props` (`TwoDogVersion`, `TwoDogNativesVersion`,
`TwoDogGodotVersion` and the companion versions); host csprojs reference
`$(TwoDogVersion)` and friends. [`2dog update`](/cli/update) rewrites that
block; [`2dog version`](/cli/version) prints what it would write.

### Under dnx

`dnx 2dog --version` never reaches the tool: `--version <VERSION>` is `dnx`'s
own option and selects which `2dog` package version to run. Use
`dnx 2dog version` to print versions and `dnx 2dog@:2dog-version:` to run a
specific tool version.
