---
title: MAUI Configuration
description: "Configure MAUI targets, GodotView rendering and publishing."
tagline: "Learn how to configure Godot in MAUI, from targets to publishing."
---

# MAUI Configuration

Set build properties in `MyGame.maui.csproj`; package versions come from the
root `Directory.Build.props`. The setup pins the .NET 10 SDK in the host's
`global.json` and in a new root `global.json`. It does not change an existing
root `global.json`. To use the host's pin, run commands from `MyGame.maui/`.

## Platform Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TargetFrameworks` | `net10.0-android`, plus `net10.0-windows10.0.19041.0` on Windows and `net10.0-ios` on a Mac | Select one with `-f`; without `-f`, `dotnet run` asks you to choose |
| `GodotProjectDir` | `..` | Game directory for import and pack export |
| `RuntimeIdentifier` | `win-x64` on Windows, `android-arm64` on Android, `ios-arm64` on iOS | Native architecture; Android also supports `android-x64`, iOS `iossimulator-arm64` |
| `TwoDogVariant` | `debug` in Debug, `release` otherwise | Native engine variant; select it with the Debug or Release configuration |
| `WindowsPackageType` | `None` | Unpackaged Windows app |
| `WindowsAppSDKSelfContained` | `true` on Windows | Bundle the Windows App SDK runtime |
| `PublishSelfContained` | `true` on Windows | Bundle the .NET runtime |
| `TwoDogAndroidGameAssembly` | Game assembly name | Assembly extracted before engine startup |
| `TwoDogIosGameAssembly` | Game assembly name | Assembly kept whole by the trimmer on iOS |
| `AndroidPackageFormats` | `apk` | Use `aab` for store distribution |

Windows builds are
[unpackaged and self-contained](https://learn.microsoft.com/dotnet/maui/windows/deployment/publish-unpackaged-cli).
Android uses the [Android host's export, signing and trimming settings](/configuration/android),
and iOS uses the [iOS host's settings](/configuration/ios). Both use Mono AOT in
Release. The MAUI host does not support Native AOT.

## View Properties

Set startup properties before you add `GodotView` to a page:

| Property                 | Default               | Purpose                                                                                       |
|--------------------------|-----------------------|-----------------------------------------------------------------------------------------------|
| `Project`                | Required              | Game assembly name                                                                            |
| `ProjectPath`            | `null`                | Windows project directory; published games use their pack                                     |
| `Arguments`              | Empty                 | Godot startup arguments                                                                       |
| `PresentationMode`       | `Auto`                | Windows: prefer GPU sharing, with CPU fallback; `Gpu` requires sharing, `Cpu` forces readback |
| `ActivePresentationMode` | `null` before startup | Read-only active GPU or CPU mode                                                              |

Windows GPU mode defaults to the Vulkan mobile renderer. `Arguments` can
override it. In Auto mode, the Compatibility renderer uses CPU presentation.
CPU readback can reduce frame rate at larger resolutions. Android always uses
Godot's native GPU surface.

## Publish

```bash
dotnet publish MyGame.maui -f net10.0-windows10.0.19041.0 -c Release
dotnet publish MyGame.maui -f net10.0-android -c Release -r android-arm64
dotnet publish MyGame.maui -f net10.0-ios -c Release -r ios-arm64 -p:CodesignKey="Apple Development"
```

See the [MAUI host guide](/hosts/maui) for setup, lifecycle and `Dispatch`.
