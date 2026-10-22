---
title: Qt Quick Configuration
description: "MSBuild and session settings for Qt Quick hosts: Qt location, the TwoDog QML module, and presentation."
tagline: "Learn how to configure Godot in your Qt Quick app, from Qt to GPU sharing."
---

# Qt Quick Configuration

Set properties in `MyGame.qt.csproj`. Reference `2dog.qt` and explicitly
select `2dog.engine` or `2dog.engine.double`. Also reference the
[Qt Bridge for C#](https://github.com/qt/qtbridge-csharp) package for the build
platform, `QtGroup.Qt.Bridge.CSharp.<rid>`.
[Global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.

## Qt Location

`QtDir` is the Qt 6 prefix, the directory containing
`lib/cmake/Qt6/Qt6Config.cmake`. The `win-x64` Qt Bridge package sets it to
its bundled Qt. On Linux and macOS, pass it to every build:

```bash
dotnet build MyGame.qt -p:QtDir=/path/to/qt6
```

The [Qt Bridge documentation](https://github.com/qt/qtbridge-csharp) describes
the settings of Qt Bridge, such as its export mode and resource packaging.

## The TwoDog QML Module

`GodotView` lives in the `TwoDog` QML module, which `2dog.qt` ships as C++
source. The build compiles it with CMake and Ninja against the Qt in
`QtDir`, in the host's `obj` directory. Then the build copies it to `TwoDog/`
next to the app, where QML finds it. It rebuilds when its sources or `QtDir`
change. On Windows,
the build uses the newest Visual Studio installation with C++ tools. Do not use
a very long path for the NuGet package folder, because the paths of Qt's CMake
files can then exceed the 260-character path limit of Windows. The default
`%USERPROFILE%\.nuget\packages` is short enough.

## Session Properties

Set startup properties when you create the `QtGodotSession`:

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

On Windows, GPU sharing needs the Direct3D 11 backend of Qt Quick, which is
the default backend there. The session makes the engine render on the same GPU
as Qt. CPU readback can
reduce the frame rate at larger window sizes.

## Publishing

Publishing the Qt Quick host is untested, and Native AOT is unsupported.

See the [Qt Quick host guide](/hosts/qt) for setup, threading and licensing.
