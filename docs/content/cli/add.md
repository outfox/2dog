---
title: 2dog add
description: "Reference for 2dog add: add .NET hosts to an existing Godot project in place - arguments, host flags, options, examples."
tagline: "Learn how to add .NET hosts to the Godot project you already have."
---

# `2dog add`

Adds hosts to an existing Godot project, in place. Run it again to add more
hosts, including a second host of the same kind. 

Without host flags it asks which hosts to add. Host flags skip that wizard;
on terminals, a missing workload (`wasm-tools`, `android`) can still be offered.
`-y` runs unattended.

```bash
2dog add [path] [hosts] [options]
```

| Argument | Meaning |
| --- | --- |
| `path` | Directory containing `project.godot`; defaults to the current directory |

## Hosts

`--repl [folder]` adds an opt-in [C# REPL host](/hosts/repl) with syntax
highlighting, completion and live access to the running game.

`--maui [folder]` adds a [MAUI host](/hosts/maui) for Windows, Android and iOS.
Install `maui-windows` on Windows, `maui-android` for Android builds and
`maui-ios` on a Mac. The host is excluded from plain solution builds.

`--ios [folder]` adds an experimental [iOS host](/hosts/ios). It builds on a
Mac with Xcode and the `ios` workload only, and is excluded from plain solution
builds.

Any [host flag](/dnx-2dog#host-flags), repeatable. Unattended without one:
generic and browser, minus the `--no-<host>` ones. Add tests explicitly with
`--xunit`, `--gdunit` or `--nunit`. Existing hosts are
recognized and skipped.

Test hosts run on Microsoft.Testing.Platform. Without a root `global.json`,
adding one creates a `global.json` that selects it, so `dotnet test` from the
project root runs every test host. An existing `global.json` is never edited;
2dog warns when it does not select Microsoft.Testing.Platform.

The new hosts are built for the running tool's versions, so the game project's
`Godot.NET.Sdk` and an existing 2dog version block in `Directory.Build.props`
are raised to them in the same run (never lowered), as `2dog update` would. A
move across Godot lines is called out: install the matching editor and open
the project once.

Migrating an existing `.sln` to `.slnx` first saves a `.sln.old` backup beside
it, using `.sln.old.1`, `.sln.old.2`, and so on if needed. Existing backups
are kept; the `.sln` is removed only after conversion succeeds.

## Options

| Option | Effect |
| --- | --- |
| `-n, --name <name>` | Base name override for the scaffolded files; letters, digits, `.` and `_` survive, `-` becomes `_`, C# keywords get a `_` prefix |
| `--rename <NewName>` | Rename a .NET project name that [contains spaces](/known-issues/spaced-project-names), then scaffold; only before any hosts exist |
| `--dry-run` | Print the plan; change nothing |
| `--force` | Overwrite scaffolded files that exist; never deletes |
| `--double-precision [true\|false]` | Select matching double precision bindings and natives for the game and all hosts; `false` selects single precision |
| `--web-threads [true\|false]` | Enable experimental multithreading for Web, WebXR and Blazor clients; serving requires COOP/COEP headers |
| `--no-restore` | Skip the final `dotnet restore` |
| `--install-wasm-tools` | Install missing `wasm-tools` for browser hosts without an installation prompt |
| `--install-android-workload` | Install the missing `android` workload for Android hosts without an installation prompt |

Build settings are also available as wizard toggles. Omitting these flags on
`add` preserves existing settings. See [build options](/dnx-2dog#build-options)
for precision and browser serving requirements.

Plus the [global and output options](/dnx-2dog#global-options).

Before restore, newly selected browser hosts get a `wasm-tools` installation offer
on terminals, and newly selected Android hosts an `android` one. Existing optional
hosts do not trigger offers when adding a desktop or test host.
`--no-restore` skips the offers; the install
flags request installation explicitly, including in CI. `--dry-run` never checks or installs workloads.

A failed restore returns exit code `2` and keeps the created files. The tool
explains recognized errors; fix the cause and run `dotnet restore` again.

## Examples

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then add
and run a generic .NET host in your existing Godot project:

```bash
cd path/to/MyGame
dnx 2dog add --generic
dotnet run --project MyGame.2dog
```

2dog creates a nested host project that runs your game. Keep editing scenes
and scripts in Godot as usual. GDScript projects work too.

Choose hosts interactively, or supply host flags:

```bash
2dog add                           # interactive, here
2dog add --generic MyGame.editor   # a second generic host, named
2dog add --android                 # an Android host
2dog add --ios                     # an iOS host (builds on a Mac)
2dog add --xunit                   # an xUnit test host
2dog add --gdunit                  # a GDScript test host (MTP)
2dog add --nunit                   # an NUnit test host
2dog add path/to/project --no-web
2dog add --rename MyGame           # fix a spaced .NET name first
```

See [Hosts](/hosts/) for host types and [Project Layout](/project-layout) for
generated files. For a new project, use [`2dog new`](/cli/new).
