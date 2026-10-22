---
title: 2dog add
description: "Reference for 2dog add: add .NET hosts to an existing Godot project in place - arguments, host flags, options, examples."
tagline: "Learn how to add .NET hosts to the Godot project you already have."
---

# `2dog add`

Adds hosts to an existing Godot project, in place. Run it again to add more
hosts, including a second host of the same kind. 

Without host flags it asks which hosts to add. Host flags skip that wizard.
On a terminal, the tool can still offer to install a missing workload
(`wasm-tools`, `android`).
`-y` runs unattended.

```bash
2dog add [path] [hosts] [options]
```

| Argument | Meaning                                                                 |
|----------|-------------------------------------------------------------------------|
| `path`   | Directory containing `project.godot`; defaults to the current directory |

## Hosts

`--maui [folder]` adds a [MAUI host](/hosts/maui) for Windows, Android and iOS.
Install `maui-windows` on Windows, `maui-android` for Android builds and
`maui-ios` on a Mac. The host is excluded from plain solution builds.

`--ios [folder]` adds an experimental [iOS host](/hosts/ios). It builds only on a
Mac with Xcode and the `ios` workload. Plain solution builds do not include it.

Use any [host flag](/dnx-2dog#host-flags). Repeat a flag to add another host of
the same kind. Without host flags, an unattended run adds the generic and Web
hosts, except the ones that you exclude with `--no-<host>`. Add tests
explicitly with `--xunit`, `--gdunit` or `--nunit`. 2dog finds existing hosts
and skips them.

If you add a test host and no root `global.json` exists, 2dog creates one that
selects [Microsoft.Testing.Platform](/testing/#test-runners). 2dog never edits
an existing `global.json`, but it warns you if that file does not select
Microsoft.Testing.Platform.

The new hosts use the versions of the running tool. In the same run, 2dog
raises the game project's `Godot.NET.Sdk` and an existing 2dog version block in
`Directory.Build.props` to these versions, as `2dog update` does. It never
lowers them. If this changes the Godot line of the project, 2dog tells you. In
that case, install the matching editor and open the project once.

When 2dog migrates an existing `.sln` to `.slnx`, it keeps the old file as a
numbered `.sln.old` backup.

## Options

| Option                             | Effect                                                                                                                                               |
|------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------|
| `-n, --name <name>`                | Override the base name of the setup's files; letters, digits, `.` and `_` stay, `-` becomes `_`, and C# keywords and leading digits get a `_` prefix |
| `--rename <NewName>`               | Rename a .NET project name that [contains spaces](/known-issues/spaced-project-names), then set up; only before any hosts exist                      |
| `--dry-run`                        | Print the plan; change nothing                                                                                                                       |
| `--force`                          | Overwrite existing files from the setup; never deletes                                                                                               |
| `--double-precision [true\|false]` | Select matching double precision bindings and natives for the game and all hosts; `false` selects single precision                                   |
| `--web-threads [true\|false]`      | Enable experimental multithreading for Web and Blazor clients; the web server must send COOP/COEP headers                                            |
| `--no-restore`                     | Skip the final `dotnet restore`                                                                                                                      |
| `--install-wasm-tools`             | Install missing `wasm-tools` for browser hosts without an installation prompt                                                                        |
| `--install-android-workload`       | Install the missing `android` workload for Android hosts without an installation prompt                                                              |

If you omit the build settings, 2dog keeps the project's settings. See
[build options](/dnx-2dog#build-options) for their requirements.

Plus the [global and output options](/dnx-2dog#global-options).

Workload offers apply only to newly selected hosts. `--no-restore` skips them;
the install flags work in CI too. `--dry-run` never checks or installs workloads.

If the restore fails, the tool returns exit code `2` and keeps the created
files. It explains the errors that it recognizes. Fix the cause. Then run
`dotnet restore` again.

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
