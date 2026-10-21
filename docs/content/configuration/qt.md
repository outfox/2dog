---
title: Qt Quick Configuration
description: "MSBuild and session settings for Qt Quick hosts: Qt location, the TwoDog QML module, and presentation."
tagline: "Learn how to configure Godot in your Qt Quick app, from Qt to GPU sharing."
---

# Qt Quick Configuration

Set properties in `MyGame.qt.csproj`. Reference `2dog.qt`, explicitly select
`2dog.engine` or `2dog.engine.double`, and reference the
[Qt Bridge for C#](https://github.com/qt/qtbridge-csharp) package for the build
platform, `QtGroup.Qt.Bridge.CSharp.<rid>`.
[Global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.

## Qt Location

`QtDir` is the Qt 6 prefix, the directory containing
`lib/cmake/Qt6/Qt6Config.cmake`. The `win-x64` Qt Bridge package sets it to
its bundled Qt; on Linux and macOS, pass it to every build:

```bash
dotnet build MyGame.qt -p:QtDir=/path/to/qt6
```

Qt Bridge's own settings, such as its export mode and resource packaging, are
described in its [documentation](https://github.com/qt/qtbridge-csharp).

## The TwoDog QML Module

`GodotView` lives in the `TwoDog` QML module, which `2dog.qt` ships as C++
source. The build compiles it against the Qt in `QtDir` with CMake and Ninja,
under the host's `obj` directory, and copies it to `TwoDog/` next to the app,
where QML finds it. It rebuilds when its sources or `QtDir` change. On Windows,
the build uses the newest Visual Studio installation with C++ tools, and a
very long NuGet package folder can push Qt's CMake files past Windows' 260
character path limit; the default `%USERPROFILE%\.nuget\packages` is fine.

## Session Properties

Set startup properties when creating the `QtGodotSession`:

| Property           | Default | Purpose                                                                                     |
|--------------------|---------|---------------------------------------------------------------------------------------------|
| `PresentationMode` | `Auto`  | Prefer GPU texture sharing with CPU fallback; `Gpu` requires sharing, `Cpu` forces readback |
| `ActiveMode`       | `null`  | Read-only: `Gpu` or `Cpu` while frames are presented                                        |
| `IsPaused`         | `false` | Gameplay pause; set on the GUI thread, or before startup to start paused                    |

```csharp
using var session = new QtGodotSession("MyGame", args: args)
{
    PresentationMode = QtPresentationMode.Cpu,
};
```

GPU sharing needs Qt Quick's Direct3D 11 backend on Windows, Qt's default
there. The session steers the engine to the GPU Qt renders on. CPU readback can
reduce the frame rate at larger window sizes.

## Publishing

Publishing the Qt Quick host is untested, and Native AOT is unsupported.

See the [Qt Quick host guide](/hosts/qt) for setup, threading and licensing.
