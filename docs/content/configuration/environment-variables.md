---
title: Environment Variables
description: "Environment variables for 2dog debugging, diagnostics, CLI output, assembly loading, and Android builds."
tagline: "Learn which environment variables help you debug and diagnose 2dog."
---

# Environment Variables

Set these in the shell that launches `dotnet`, or in your IDE's launch
environment. Restart an already-running IDE after changing its inherited
environment. Diagnostic switches are off when unset.

## Debugging and Diagnostics

| Variable | Value | Effect |
| --- | --- | --- |
| `GODOT4` | Full path to the .NET-enabled Godot executable | Used by the generated VS Code C# launch configuration. On Windows, select the main `mono` executable, not the `_console.exe` wrapper. Checked by `2dog doctor` |
| `TWODOG_LIFECYCLE_LOG` | Log file path | Traces window activation, minimize/restore messages, Godot window signals and an independent engine-loop heartbeat. Applies to windowed engines using the Windows display driver |
| `TWODOG_GODOT_LOG_DIR` | Directory path | Makes 2dog's xUnit/NUnit fixtures enable Godot's verbose native log and write a separate `godot-<process-id>-<fixture-number>.log` per fixture. Creates the directory if needed; works with headless and windowed fixtures |
| `TWODOG_AVALONIA_DIAG` | Exactly `1` | Prints Avalonia GPU presentation and Linux EGL interop diagnostics to the console, prefixed with `2DOG_GPU:` or `2DOG_EGL:` |

`GODOT4` selects the executable launched by the debugger. Resource import and
pack export use the packaged editor by default; choose an external editor with
the explicit [`GodotEditor` MSBuild property](/import-tool#properties).
Ambient `GODOT4` and `GODOT_EDITOR` variables do not select the import/export
editor.

For lifecycle tracing, choose a file in an existing writable directory and
use a different path for each simultaneously running process. The log appends
and flushes once per second, including while the engine thread is stalled.
It records message handling and iteration progress; check the game visually
to confirm rendering resumes. Tracing does not activate or restore the window.

For Avalonia console diagnostics on Windows, use `OutputType=Exe`; see
[Avalonia Configuration](./avalonia).

### Capture a Windows Lifecycle Trace

Run from the directory containing `MyGame.2dog`:

::: code-group

```powershell [PowerShell]
$env:TWODOG_LIFECYCLE_LOG = Join-Path $PWD 'window-lifecycle.log'
dotnet run --project MyGame.2dog
Remove-Item Env:TWODOG_LIFECYCLE_LOG
```

```nushell [Nushell]
$env.TWODOG_LIFECYCLE_LOG = ($env.PWD | path join 'window-lifecycle.log')
dotnet run --project MyGame.2dog
hide-env TWODOG_LIFECYCLE_LOG
```

:::

Use `TWODOG_GODOT_LOG_DIR` to capture native test output separately:

```bash
TWODOG_GODOT_LOG_DIR=./godot-logs dotnet test MyGame.xunit
```

In PowerShell, set `$env:TWODOG_GODOT_LOG_DIR = './godot-logs'` before running
`dotnet test`, then remove it with `Remove-Item Env:TWODOG_GODOT_LOG_DIR`.

## CLI Output and Automation

These variables affect the [`2dog` CLI](/dnx-2dog).

| Variable | Value | Effect |
| --- | --- | --- |
| `TWODOG_ACCESSIBLE` | `1` | Uses numbered yes/no questions instead of selection lists, and static progress instead of spinners. Equivalent to `--accessible` |
| `NO_COLOR` | Any nonempty value, including `0` | Disables colors. Equivalent to `--no-color` |
| `CLICOLOR_FORCE`, `FORCE_COLOR` | `1` | Enables colors even when output is redirected; `NO_COLOR`, `--no-color` and plain mode still disable colors |
| `TERM` | `dumb` | Enables plain output without ANSI sequences and uses accessible prompts. Equivalent to `--plain` |
| `CI` | `1` | Disables interactive prompts and animated progress |

The CLI also detects `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`,
`TEAMCITY_VERSION`, `BUILD_NUMBER`, `JENKINS_URL`, `BUILDKITE`, `CIRCLECI`,
`TRAVIS` and `APPVEYOR` as CI indicators. For these indicators,
`TWODOG_ACCESSIBLE`, `CLICOLOR_FORCE` and `FORCE_COLOR`, any nonempty value
except `0` or `false` enables the switch (`false` is case-insensitive).

## Godot Assembly Paths

Packaged hosts normally configure these paths automatically. Use matching
Godot assemblies when supplying a custom layout.

| Variable | Value | Effect |
| --- | --- | --- |
| `GODOTSHARP_DIR` | Directory containing `GodotPlugins.dll` | Overrides the API source directory during the build when the file exists. The hosted loader also probes it; during engine startup, 2dog selects the packaged flat or nested API layout when available |
| `GODOT_TOOLS_DIR` | Directory containing `GodotTools.dll` | Overrides the fork's editor tools directory when the file exists. Import/export helpers receive the packaged tools path automatically |
| `GODOT_PROJECT_ASSEMBLY_DIR` | Directory containing the game's C# assembly | Used by the fork to find the hosted game assembly. Engine startup sets it from `Engine.ProjectAssemblyDir`, defaulting to the host output directory; configure that property for a custom location |

## .NET and Android Tooling

| Variable | Value | Effect |
| --- | --- | --- |
| `DOTNET_HOST_PATH` | Full path to `dotnet` | First choice for CLI child `dotnet` commands when the file exists; usually supplied by the .NET launcher |
| `DOTNET_ROOT` | .NET installation directory | Next location checked for `dotnet` |
| `PATH` | Executable search directories | Final fallback when the CLI locates `dotnet` |
| `AndroidSdkDirectory` | Android SDK directory | First explicit SDK location checked by `2dog new`, `add` and `doctor` |
| `ANDROID_HOME`, `ANDROID_SDK_ROOT` | Android SDK directory | Additional SDK locations checked in that order. `ANDROID_SDK_ROOT` is deprecated; keep both paths consistent if both are set |
| `JAVA_HOME` | JDK directory | Used by Android tooling; the repository's APK build script forwards it as `JavaSdkDirectory` |
| `ANDROID_USER_HOME` | Android user configuration directory | Location of `debug.keystore` for the signing fallback; defaults to `~/.android` |
| `ANDROID_NDK_ROOT`, `ANDROID_NDK_HOME` | Android NDK directory | Used in that order by the repository's APK build script; otherwise it searches SDK `ndk` directories |

For Android signing, use the following sets together:

| Build variant | Keystore path | Key alias | Keystore/key password |
| --- | --- | --- | --- |
| Debug | `GODOT_ANDROID_KEYSTORE_DEBUG_PATH` | `GODOT_ANDROID_KEYSTORE_DEBUG_USER` | `GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD` |
| Release | `GODOT_ANDROID_KEYSTORE_RELEASE_PATH` | `GODOT_ANDROID_KEYSTORE_RELEASE_USER` | `GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD` |

Explicit .NET signing settings with `AndroidKeyStore=true` take precedence.
When a Godot keystore path is set, its alias and password must also be set.
2dog passes the password to the signer through an `env:` reference. Otherwise,
signing falls back to the Android user debug keystore, then .NET's debug key.
See [Android Configuration](./android) and the [Android host guide](/hosts/android).

## Repository Test Helpers

These settings apply to development scripts in a 2dog source checkout.

| Variable | Value | Effect |
| --- | --- | --- |
| `TWODOG_CDP_DEBUG` | Exactly `1` | Enables protocol diagnostics in the browser smoke-test waiter |
| `TWODOG_TEST_VULKAN_LOADER` | Path to `vulkan-1.dll` | Copies that Vulkan loader beside the Windows NUnit rendering test host; set by the software-rendering setup script |
| `TWODOG_TEST_RESTORE_PACKAGES` | NuGet cache directory | Overrides the restore cache used by the Android package tests |
| `TWODOG_WEB_TARGETS` | Path to browser MSBuild targets | Overrides the targets file exercised by the web side-module export tests |
