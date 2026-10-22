---
title: Environment Variables
description: "Environment variables for 2dog debugging, diagnostics, CLI output, assembly loading, and Android builds."
tagline: "Learn which environment variables help you debug and diagnose 2dog."
---

# Environment Variables

Diagnostic switches are off when unset.

## Debugging and Diagnostics

| Variable               | Value                                          | Effect                                                                                                                                                                                                                     |
|------------------------|------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `GODOT4`               | Full path to the .NET-enabled Godot executable | The generated VS Code C# launch configuration uses it. On Windows, select the main `mono` executable, not the `_console.exe` wrapper. `2dog doctor` checks it                                                              |
| `TWODOG_LIFECYCLE_LOG` | Log file path                                  | Traces window activation, minimize/restore messages, Godot window signals and an independent engine-loop heartbeat. Applies to windowed engines using the Windows display driver                                           |
| `TWODOG_GODOT_LOG_DIR` | Directory path                                 | Makes 2dog's xUnit/NUnit fixtures enable Godot's verbose native log and write a separate `godot-<process-id>-<fixture-number>.log` per fixture. Creates the directory if needed; works with headless and windowed fixtures |
| `TWODOG_AVALONIA_DIAG` | Exactly `1`                                    | Prints Avalonia GPU presentation and Linux EGL interop diagnostics to the console, prefixed with `2DOG_GPU:` or `2DOG_EGL:`                                                                                                |

`GODOT4` selects the executable launched by the debugger. Resource import and
pack export use the packaged editor by default. To use an external editor, set
the [`GodotEditor` MSBuild property](/import-tool#properties) explicitly.
`GODOT4` and `GODOT_EDITOR` in the environment do not select the editor for
import and export.

The lifecycle log appends and flushes once per second, even while the engine
thread is stalled. Give each process its own log file. On Windows, Avalonia
diagnostics need `OutputType=Exe`.

```bash
TWODOG_LIFECYCLE_LOG=./window-lifecycle.log dotnet run --project MyGame.2dog
TWODOG_GODOT_LOG_DIR=./godot-logs dotnet test MyGame.xunit
```

## CLI Output and Automation

These variables affect the [`2dog` CLI](/dnx-2dog).

| Variable                        | Value                             | Effect                                                                                                                           |
|---------------------------------|-----------------------------------|----------------------------------------------------------------------------------------------------------------------------------|
| `TWODOG_ACCESSIBLE`             | `1`                               | Uses numbered yes/no questions instead of selection lists, and static progress instead of spinners. Equivalent to `--accessible` |
| `NO_COLOR`                      | Any nonempty value, including `0` | Disables colors. Equivalent to `--no-color`                                                                                      |
| `CLICOLOR_FORCE`, `FORCE_COLOR` | `1`                               | Enables colors even when output is redirected; `NO_COLOR`, `--no-color` and plain mode still disable colors                      |
| `TERM`                          | `dumb`                            | Enables plain output without ANSI sequences and uses accessible prompts. Equivalent to `--plain`                                 |
| `CI`                            | `1`                               | Disables interactive prompts and animated progress                                                                               |

The CLI also detects `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`,
`TEAMCITY_VERSION`, `BUILD_NUMBER`, `JENKINS_URL`, `BUILDKITE`, `CIRCLECI`,
`TRAVIS` and `APPVEYOR` as CI indicators. For these indicators and for
`TWODOG_ACCESSIBLE`, `CLICOLOR_FORCE` and `FORCE_COLOR`, any value enables the
switch, except an empty value, `0` or `false`. Case does not matter for
`false`.

## Godot Assembly Paths

`GODOTSHARP_DIR` (the directory holding `GodotPlugins.dll`), `GODOT_TOOLS_DIR`
and `GODOT_PROJECT_ASSEMBLY_DIR` locate Godot's managed assemblies. 2dog sets
them for its hosts, so leave them unset. For a game assembly in a custom
location, use [`Engine.ProjectAssemblyDir`](/api/engine#projectassemblydir). [`2dog doctor`](/cli/doctor) reports stray values as
`env.overrides`.

## .NET and Android Tooling

| Variable                                  | Value                                                                                                        | Effect                                                                                                                             |
|-------------------------------------------|--------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------|
| `DOTNET_HOST_PATH`, `DOTNET_ROOT`, `PATH` | See [.NET environment variables](https://learn.microsoft.com/dotnet/core/tools/dotnet-environment-variables) | Where the CLI finds `dotnet` for child commands, in that order                                                                     |
| `AndroidSdkDirectory`                     | Android SDK directory                                                                                        | First explicit SDK location checked by `2dog new`, `add` and `doctor`                                                              |
| `ANDROID_HOME`, `ANDROID_SDK_ROOT`        | Android SDK directory                                                                                        | Additional SDK locations checked in that order; see [Android environment variables](https://developer.android.com/tools/variables) |
| `ANDROID_USER_HOME`                       | Android user configuration directory                                                                         | Location of `debug.keystore` for the signing fallback; defaults to `~/.android`                                                    |

For Android signing, set all three variables of a row together:

| Build variant | Keystore path                         | Key alias                             | Keystore/key password                     |
|---------------|---------------------------------------|---------------------------------------|-------------------------------------------|
| Debug         | `GODOT_ANDROID_KEYSTORE_DEBUG_PATH`   | `GODOT_ANDROID_KEYSTORE_DEBUG_USER`   | `GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD`   |
| Release       | `GODOT_ANDROID_KEYSTORE_RELEASE_PATH` | `GODOT_ANDROID_KEYSTORE_RELEASE_USER` | `GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD` |

2dog passes the password to the signer through an `env:` reference. See
[Android signing](./android#signing) for the keystore order.
