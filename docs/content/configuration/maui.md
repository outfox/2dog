---
title: MAUI Configuration
description: "Configure MAUI targets, GodotView rendering and publishing."
tagline: "Learn how to configure Godot in MAUI, from targets to publishing."
---

# MAUI Configuration

Set build properties in `MyGame.maui.csproj`; package versions come from the
root `Directory.Build.props`. Scaffolding adds .NET 10 SDK pins, preserving an
existing root `global.json`. Run from `MyGame.maui/` to use the host's pin.

## Platform Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TargetFrameworks` | `net10.0-android`, plus `net10.0-windows10.0.19041.0` on Windows | Select with `-f`; `dotnet run` asks without it |
| `GodotProjectDir` | `..` | Game directory for import and pack export |
| `RuntimeIdentifier` | `win-x64` on Windows, `android-arm64` on Android | Native architecture; Android also supports `android-x64` |
| `TwoDogVariant` | `debug` in Debug, `release` otherwise | Use Debug or Release |
| `WindowsPackageType` | `None` | Unpackaged Windows app |
| `WindowsAppSDKSelfContained` | `true` on Windows | Bundle the Windows App SDK runtime |
| `PublishSelfContained` | `true` on Windows | Bundle the .NET runtime |
| `TwoDogAndroidGameAssembly` | Game assembly name | Assembly extracted before engine startup |
| `AndroidPackageFormats` | `apk` | Use `aab` for store distribution |

Android uses the [Android host's export, signing and trimming settings](/configuration/android)
and Mono AOT in Release. Native AOT is unsupported by the MAUI host.

## View Properties

Set startup properties before adding `GodotView` to a page:

| Property | Default | Purpose |
| --- | --- | --- |
| `Project` | Required | Game assembly name |
| `ProjectPath` | `null` | Windows project directory; published games use their pack |
| `Arguments` | Empty | Godot startup arguments |
| `PresentationMode` | `Auto` | Windows: prefer GPU sharing, with CPU fallback; `Gpu` requires sharing, `Cpu` forces readback |
| `ActivePresentationMode` | `null` before startup | Read-only active GPU or CPU mode |

Windows GPU mode defaults to the Vulkan mobile renderer. `Arguments` can
override it; the Compatibility renderer uses CPU presentation in Auto mode.
CPU readback can reduce frame rate at larger resolutions. Android always uses
Godot's native GPU surface.

## Publish

```bash
dotnet publish MyGame.maui -f net10.0-windows10.0.19041.0 -c Release
dotnet publish MyGame.maui -f net10.0-android -c Release -r android-arm64
```

See the [MAUI host guide](/hosts/maui) for setup, lifecycle and `Dispatch`.
