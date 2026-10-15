---
title: MAUI Configuration
description: "Configure the Windows and Android targets, native packages, and rendering policy of a 2dog MAUI host."
---

# MAUI Configuration

Set properties in `MyGame.maui.csproj`. It references `2dog.engine` and
`2dog.maui` at `$(TwoDogVersion)`, and `Microsoft.Maui.Controls` at
`$(TwoDogMauiVersion)` from the generated root `Directory.Build.props`.

## Platform Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TargetFrameworks` | `net10.0-android`, plus `net10.0-windows10.0.19041.0` on Windows | Supported MAUI targets; build with `-f` |
| `GodotProjectDir` | `..` | Game project used for resource import and pack export |
| `RuntimeIdentifier` | `win-x64` on Windows, `android-arm64` on Android | Native architecture; Android also supports `android-x64` |
| `TwoDogVariant` | `debug` in Debug, `release` otherwise | Native engine variant; build the MAUI host in Debug or Release |
| `WindowsPackageType` | `None` | Unpackaged Windows application |
| `WindowsAppSDKSelfContained` | `true` on Windows | Carry the Windows App SDK runtime |
| `PublishSelfContained` | `true` on Windows | Carry the .NET runtime in published Windows output |
| `TwoDogAndroidGameAssembly` | Game assembly name | Assembly extracted before Android engine startup |
| `AndroidPackageFormats` | `apk` | Sideloadable Android package; choose `aab` for store distribution |

The Android target references `2dog.android` and the native meta package for
the selected RID, exact-pinned to `$(TwoDogNativesVersion)`. It uses the
same game pack export, signing, and trimming targets as the
[Android host](/configuration/android). Native AOT is not supported by this
MAUI host. Android Release uses Mono AOT.

## View Properties

Set these before adding `GodotView` to a page:

| Property | Default | Purpose |
| --- | --- | --- |
| `Project` | Required | Game assembly name |
| `ProjectPath` | `null` | Windows project directory; packaged games use their pack |
| `Arguments` | Empty | Extra Godot startup arguments |
| `PresentationMode` | `Auto` | Windows GPU preference and CPU fallback policy |
| `ActivePresentationMode` | `null` before startup | Read the active GPU or CPU presentation mode |

Android always uses Godot's native GPU surface. Windows GPU mode selects
the Vulkan mobile renderer by default; additional `Arguments` can override
engine options. A Compatibility renderer uses CPU presentation in Auto mode.

## Publish

```bash
dotnet publish MyGame.maui -f net10.0-windows10.0.19041.0 -c Release
dotnet publish MyGame.maui -f net10.0-android -c Release -r android-arm64
```

For workload setup, the view lifecycle, and thread-safe commands, see the
[MAUI host guide](/hosts/maui).
