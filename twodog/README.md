# 2dog

The .NET Tool for [2dog](https://2dog.dev), which runs Godot as a library from your own
.NET entry point. Point it at a Godot project and it adds .NET hosts: desktop, web, WebXR, Avalonia, Qt Quick,
Blazor, WinForms, WinUI 3, MAUI, Android, a C# REPL, and xUnit, NUnit or gdUnit tests. Then it keeps them healthy with
`2dog doctor` and current with `2dog update`.

Doctor warns about custom or prerelease engine versions and checks them on a
best-effort basis. It checks version consistency and the numeric Godot line,
but skips public release upgrade comparisons and cannot verify a fork's binary
compatibility with its GDExtensions.

The `dotnet new` templates ship separately as [`2dog.templates`](https://www.nuget.org/packages/2dog.templates).
To reference the engine from your own code, use [`2dog.engine`](https://www.nuget.org/packages/2dog.engine) instead.

## Getting started

With the .NET 10 SDK, run it without installing:

```bash
cd path/to/MyGame        # the folder with project.godot
dnx 2dog add             # pick hosts interactively
dotnet run --project MyGame.2dog
```

Or start from scratch with `dnx 2dog new MyGame`. Every prompt has a flag, so this runs unattended:

```bash
dnx 2dog add --web --xunit --yes
```

Other ways in: `dotnet tool install -g 2dog` installs the `2dog` command, and
`dotnet new install 2dog.templates && dotnet new 2dog -n MyGame` uses the template.

Your Godot project stays where it is. Each host is a nested folder with a `.gdignore`, added to the
solution beside your game:

```
MyGame/
  project.godot
  MyGame.csproj      <- created or minimally patched
  MyGame.slnx        <- created, or an existing .sln is migrated (backup kept)
  MyGame.2dog/       <- desktop host
  MyGame.web/        <- browser host
  MyGame.xunit/      <- tests
```

The tool never moves, renames or deletes your files, except for the announced `.sln` migration and `--rename`.
Run it again to add more hosts; existing ones are left alone. The
[guide](https://2dog.dev/getting-started) and [CLI reference](https://2dog.dev/dnx-2dog) cover the details.

## Commands

| Command | Effect |
| --- | --- |
| `2dog` | Print version info and usage |
| `2dog new [Name] [dir]` | Create a new Godot project with 2dog hosts |
| `2dog add [path]` | Add hosts to an existing Godot project |
| `2dog doctor [path]` | Check the project and this machine; `--fix` applies the safe fixes; `--build` / `--log` explain build failures |
| `2dog update [path]` | Update the project's 2dog packages to this tool's versions (never downgrades) |
| `2dog pack list <file.pck>` | List a `.pck`'s contents by size (no engine involved) |
| `2dog repl` | Attach this terminal to a running REPL host (`dotnet run --project MyGame.repl`) |
| `2dog repl exec [code]` | Run C# and node commands in a running REPL host; prints values, exits nonzero on failure |
| `2dog repl sessions`, `2dog repl close <session>` | List a REPL host's named sessions, close one |
| `2dog skill [path]` | Write the REPL working guide for coding agents: `--claude` (default, `.claude/skills/2dog-repl/SKILL.md`), `--codex` (a section of `AGENTS.md`) |
| `2dog version` | Print the tool and package versions |
| `2dog help [verb]` | Usage, or the help for one verb |

## Options

| Option | Effect |
| --- | --- |
| `--generic [folder]`, `--web [folder]`, `--webxr [folder]`, `--xunit [folder]`, `--nunit [folder]`, `--gdunit [folder]`, `--repl [folder]`, `--winforms [folder]`, `--winui [folder]`, `--avalonia [folder]`, `--qt [folder]`, `--blazor [folder]`, `--android [folder]`, `--maui [folder]`, `--ios [folder]` | Add a host, optionally in a named folder (repeatable; only `--generic` and `--web` are in the default set) |
| `--no-generic`, `--no-web` | Leave a host out of the default set |
| `-n, --name <BaseName>` | Project name (`new`) or base name override |
| `--rename <NewName>` | Fix a .NET project name that contains spaces (`add`, before any hosts exist) |
| `-o, --output <dir>` | Directory for a new project |
| `-y, --yes`, `--non-interactive` | Do not prompt; take the flags and defaults |
| `--dry-run` | Print planned actions without changing anything |
| `--force` | Overwrite files that already exist (never deletes/moves) |
| `--double-precision [true\|false]` | Select matching double precision bindings and natives for the game and all hosts; `false` selects single precision |
| `--web-threads [true\|false]` | Enable experimental multithreading for Web and Blazor clients; serving requires COOP/COEP headers |
| `--no-restore` | Skip the final `dotnet restore` |
| `--install-wasm-tools` | `new`, `add`, `doctor`, `update`: install missing `wasm-tools` for browser hosts |
| `--install-android-workload` | `new`, `add`, `doctor`, `update`: install the missing `android` workload for Android hosts |
| `--update-workloads` | `update`: install missing workloads the hosts need, then update all installed workloads for the project's SDK |
| `--allow-dirty` | `update`: proceed with uncommitted git changes |
| `--fix`, `--fix-all`, `--build [target]`, `-c, --configuration <Cfg>`, `--log <file>`, `--ignore <id>`, `--strict`, `--offline`, `--list-checks` | `doctor` options; see `2dog doctor --help` |
| `-s, --session <name>`, `--timeout <seconds>`, `--strict`, `--pipe <name>` | `repl exec` options: a named session that keeps its state, a time limit, failing on logged Godot errors, and which host to use; see `2dog repl --help` |
| `--json`, `-q, --quiet`, `--plain`, `--no-color`, `--accessible`, `-v, --verbose` | Output modes: machine-readable, terse, no styling, screen-reader friendly, extra detail |
| `-h, --help`, `--version` | Help (per verb after a verb), versions |

Exit codes: `0` ok, `1` usage error, `2` tool error (including a failed restore, which keeps the created files),
`3` doctor findings remain or a `repl exec` submission failed, `124` `repl exec` timed out, `130` cancelled.
Nothing prompts in a pipe or in CI. Workloads, troubleshooting and every verb in depth:
[2dog.dev/dnx-2dog](https://2dog.dev/dnx-2dog).
