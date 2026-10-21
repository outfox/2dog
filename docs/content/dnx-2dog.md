---
title: dnx 2dog
description: "Reference for the 2dog command-line tool: installation, commands, host flags, global and output options, exit codes, version pinning."
tagline: "Learn how to install and use the 2dog command-line tool."
---

# `dotnet tool execute 2dog`

`2dog` scaffolds .NET host projects around a Godot project, keeps them healthy,
and updates them. 

![Illus. Moritz Voss: a white anthro dog in a hacker hoodie getting pulled on the leash by their blue robot dog](img/2dog-terminal.png)
*"Everything at my pawtips!"*

Your project directory stays the solution root; added hosts live in nested
folders that Godot ignores.

## Installation

```bash
dnx 2dog add                  # run latest from nuget
dotnet tool install -g 2dog   # or install it globally
```

`dnx 2dog` creates files and edits `*.csproj`, `project.godot`, and if needed, the solution and `Directory.Build.props` in place. 

Only two changes rename files, and the tool asks before either:

- `.sln` → `.slnx` migration (renames the `.sln` to `.sln.old`)
- [`--rename`](/known-issues/spaced-project-names), which renames the .NET project, its csproj and
  `assembly_name` to a name without spaces

The tool ships as [`2dog`](https://www.nuget.org/packages/2dog/). The matching
`dotnet new` template ships separately as
[`2dog.templates`](https://www.nuget.org/packages/2dog.templates/).

## Commands

| Command                        | Use it to                                                                                                     |
|--------------------------------|---------------------------------------------------------------------------------------------------------------|
| [`2dog new`](/cli/new)         | Create a Godot project with 2dog hosts                                                                        |
| [`2dog add`](/cli/add)         | Add hosts to an existing Godot project                                                                        |
| [`2dog doctor`](/cli/doctor)   | Check the project and this machine, fix what can be fixed, explain build failures                             |
| [`2dog update`](/cli/update)   | Bring the project's 2dog packages to this tool's versions                                                     |
| [`2dog pack`](/cli/pack)       | List a `.pck`'s contents                                                                                      |
| [`2dog repl`](/cli/repl)       | Attach to a running REPL host, or run code in it from scripts and agents (`--session`, `--timeout`, `--pipe`) |
| [`2dog skill`](/cli/skill)     | Write the REPL working guide for coding agents (`--claude`, `--codex`)                                        |
| [`2dog version`](/cli/version) | Print the tool and package versions                                                                           |
| [`2dog help`](/cli/help)       | Show the usage, or the help for one verb                                                                      |

`2dog` alone prints the version info and the usage. `new`, `add` and `doctor`
prompt on a terminal when no deciding flag is given. Host flags skip the project
wizard; a missing workload (`wasm-tools`, `android`) can still be offered for
newly selected hosts before restore. `--yes`, pipes, and CI skip workload offers. Use `--install-wasm-tools` or
`--install-android-workload` to request installation explicitly, or
`2dog update --update-workloads` to update workloads.

## Host flags

`new` and `add` take one flag per host. The folder is optional; a repeated
flag adds a second host of the same kind. Test hosts are opt-in: choose
`--xunit`, `--gdunit` or `--nunit`, or select a framework in the interactive picker.

| Flag                       | Host                                                                      |
|----------------------------|---------------------------------------------------------------------------|
| `--generic [folder]`       | [2dog (generic .NET)](/hosts/generic) with your own `Main()`              |
| `--web [folder]`           | [Web host](/hosts/web)                                                    |
| `--webxr [folder]`         | [WebXR host](/hosts/webxr)                                                |
| `--xunit [folder]`         | [xUnit test project](/hosts/xunit)                                        |
| `--repl [folder]`          | [C# REPL host](/hosts/repl)                                               |
| `--gdunit [folder]`        | [GdUnit GDScript test project](/hosts/gdunit)                             |
| `--nunit [folder]`         | [NUnit test project](/hosts/nunit)                                        |
| `--winforms [folder]`      | [WinForms host](/hosts/winforms); Windows-only                            |
| `--winui [folder]`         | [WinUI 3 host](/hosts/winui); builds only on Windows                      |
| `--avalonia [folder]`      | [Avalonia host](/hosts/avalonia)                                          |
| `--qt [folder]`            | [Qt Quick host](/hosts/qt); needs CMake, Ninja and a C++ toolchain        |
| `--blazor [folder]`        | [Blazor Web App host](/hosts/blazor); needs wasm-tools                    |
| `--maui [folder]`          | [MAUI host](/hosts/maui); Windows, Android and iOS, needs MAUI workloads       |
| `--android [folder]`       | [Android host](/hosts/android); experimental, needs the android workload  |
| `--ios [folder]` | [iOS host](/hosts/ios); experimental, needs the ios workload and a Mac with Xcode |
| `--no-generic`, `--no-web` | Leave a host out of the default set (every kind has a `--no-<host>` form) |

All hosts except `--generic` and `--web` are opt-in: an unattended run never
adds them.

## Build options

`new` and `add` offer build option toggles after the host picker. Flags use the
same settings without a prompt:

| Flag                               | Effect                                                                                    |
|------------------------------------|-------------------------------------------------------------------------------------------|
| `--double-precision [true\|false]` | Use matching `.double` engine, bindings and native packages across the game and all hosts |
| `--web-threads [true\|false]`      | Enable experimental multithreading for Web and Blazor clients                             |

New projects default to single precision and single-threaded web builds.
Omitting these flags on `add` preserves the project's settings. Pass `false`
to disable a setting. Double precision needs a matching double precision Godot
editor. Threaded browser builds need cross-origin isolation headers:
`Cross-Origin-Opener-Policy: same-origin` and
`Cross-Origin-Embedder-Policy: require-corp`.
See [web multithreading](/hosts/web#multithreading) for serving requirements.

## Global options

Valid under every verb, also before it (`2dog --json add`).

| Option       | Effect                                                                                                          |
|--------------|-----------------------------------------------------------------------------------------------------------------|
| `-y, --yes`  | Do not prompt; take the flags and defaults (also `--non-interactive`, `--no-input`)                             |
| `-h, --help` | Show help; after a verb, the help for that verb                                                                 |
| `--version`  | Same as [`2dog version`](/cli/version); see [choosing a tool version](/cli/pinning#run-a-specific-tool-version) |

Values attach or follow: `--name Foo`, `--name=Foo`. `--` ends option parsing.
Mistyped options and verbs get a suggestion.

## Output

| Option          | Effect                                                                                           |
|-----------------|--------------------------------------------------------------------------------------------------|
| `--json`        | One JSON document on stdout, nothing on stderr, also on failure (`"ok": false`); implies `--yes` |
| `-q, --quiet`   | Results and problems only: no header, plan, progress or next steps                               |
| `--plain`       | No colour, no cursor movement, ASCII markers (also `TERM=dumb`)                                  |
| `--no-color`    | No colour; cursor movement stays (also `NO_COLOR`)                                               |
| `--accessible`  | Numbered yes/no questions instead of lists, no spinners (also `TWODOG_ACCESSIBLE=1`)             |
| `-v, --verbose` | Extra detail on stderr: subprocess command lines and output, stack traces                        |

The report goes to stdout, diagnostics (`error:`, `warning:`, `note:`, `hint:`,
`verbose:`, spinners) to stderr: `2dog add --json | jq .` and
`2dog doctor 2>errors.log` both work. In a pipe or a CI environment nothing
prompts or animates and the markers are ASCII; a wanted prompt falls
back to the default with a `note:`. See
[CLI environment variables](/configuration/environment-variables#cli-output-and-automation)
for the CI indicators and colour overrides.

## Exit codes

| Code  | Meaning                                                                                                                                                                                        |
|-------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `0`   | Success, including `--dry-run`, "Nothing to do", and a declined plan                                                                                                                           |
| `1`   | Usage error: unknown option or verb, missing value, wrong positional count; the error names the verb's `--help`                                                                                |
| `2`   | Tool error: project state, I/O, invalid XML, a failed subprocess, a plan that stopped half-way (the report says which steps stand), an unexpected exception (`--verbose` adds the stack trace) |
| `3`   | `doctor`: findings remain after the fixes (errors; warnings too under `--strict`), or the `--build` failed; `repl exec`: a submission failed                                                   |
| `124` | `repl exec`: `--timeout` cancelled a submission                                                                                                                                                |
| `130` | Cancelled with Ctrl+C                                                                                                                                                                          |

## Versions

The [Version guide](/cli/pinning) explains how to run a specific tool release,
print its version, and keep your project's package versions fixed.
