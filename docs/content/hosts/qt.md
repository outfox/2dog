---
title: Qt Quick
description: "Embed Godot in a Qt Quick (QML) app built with Qt Bridge for C#, with QML items over the game."
tagline: "Learn how to embed Godot in a Qt Quick app, with QML right on top."
---

# Qt Quick

The Qt Quick host uses `2dog.qt` and
[Qt Bridge for C#](https://github.com/qt/qtbridge-csharp) to show Godot inside a
QML scene. QML items placed
over the `GodotView` render on top of the game, including translucent items.

![Illus. Diego Rago: a white anthro dog in a hacker hoodie and glasses walking their blue godot robot dog](../img/2dog-harmony.png)
*(QML on top, Godot underneath, C# all the way down)*

::: warning Experimental
Qt Bridge for C# is pre-release software. The Qt Quick host is tested on
Windows; Linux and macOS builds are untested.
:::

## Use It

Qt Bridge compiles native code during the build. Install these tools first:

- [CMake](https://cmake.org/download/)
- [Ninja](https://ninja-build.org/)
- A C++ toolchain. On Windows, use Visual Studio with the
  [Desktop development with C++](https://learn.microsoft.com/cpp/build/vscpp-step-0-installation)
  workload.

See the [Qt Bridge requirements](https://github.com/qt/qtbridge-csharp) for
details.

Run these commands from your Godot project directory:

```bash
dnx 2dog add --qt
dotnet run --project MyGame.qt
```

The Qt Bridge package for Windows x64 includes Qt. On Linux and macOS,
[install Qt 6](https://doc.qt.io/qt-6/get-and-install-qt.html) first. Then
pass its prefix, which is the directory that contains
`lib/cmake/Qt6/Qt6Config.cmake`:

```bash
dotnet run --project MyGame.qt -p:QtDir=/path/to/qt6
```

## Customize It

A `QtGodotSession` owns the engine. Create it in `Main` before you load QML.
Dispose it after the QML engine exits:

```csharp
using Qt.Quick;
using twodog;

using var session = new QtGodotSession("MyGame", args: args);
Qml.LoadFromRootModule("Main");
Qml.WaitForExit();
```

The `TwoDog` QML module provides `GodotView`, which shows the game and
forwards input:

```qml
import QtQuick
import TwoDog

Window {
    width: 1280
    height: 720
    visible: true

    GodotView { anchors.fill: parent; focus: true }
}
```

Qt Bridge runs `Main` on its own thread, while Qt's GUI thread pumps the engine
once per Qt Quick frame. The session's `Started`, `FrameAdvanced`,
`ActiveModeChanged` and `Stopped` events run on the GUI thread. C# methods
that QML calls also run on the GUI thread. Both can use the Godot API
directly, for example through `session.Engine.Tree`. See
[C# and QML](https://github.com/qt/qtbridge-csharp/blob/dev/docs/csharp-and-qml.md)
to learn how to expose C# types to QML.

See the [showcase host](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.qt)
for a pause button, time-scale slider, and FPS display.

## Rendering and Lifecycle

On Windows, Qt Quick's default Direct3D 11 backend shares a GPU texture with
the engine, with no copy through the CPU. Other platforms and graphics
backends use CPU readback, which is also the automatic fallback. Set
[`PresentationMode`](/configuration/qt#session-properties) to force either.

The engine starts when the first `GodotView` renders, and it stops when the
application quits. When Godot quits, the application also quits. Only one
`GodotView` can show the engine at a time.

While the window is minimized or hidden, the engine continues to run at a
background rate but does not render, like a standalone Godot game. This
happens only while Qt's event loop runs. To pause it instead, set `IsPaused` from a QML handler
for [`Qt.application.state`](https://doc.qt.io/qt-6/qml-qtqml-qt.html#application-prop)
or the window's visibility.

`GodotView` forwards mouse, wheel and keyboard input. Physical keycodes assume
a US keyboard layout. IME composition is not supported. Captured mouse modes
hide the cursor but do not confine it.

## Publish It

Qt Bridge deploys Qt next to the app during the build. `dotnet publish` is not
tested for the Qt Quick host yet. Native AOT is unsupported, because Qt Bridge loads the app
through the .NET host.

See [Qt Quick Configuration](/configuration/qt) for build settings.

## Licensing

Qt Bridge for C# and Qt are available under the
[LGPL-3.0 or a commercial license](https://www.qt.io/licensing/), and Qt
Bridge is pre-release software. `2dog.qt` ships no Qt code; like the rest of
2dog, it is MIT licensed.

An app that ships Qt and Qt Bridge binaries must meet their license
obligations. Under the LGPL, you must:

- Include the license texts and notices.
- Make the corresponding Qt source available.
- Let users replace the LGPL libraries.

Qt Bridge compiles its generated native host from LGPL sources, so keep that
host rebuildable. This is not legal advice. Check the
[Qt Bridge repository](https://github.com/qt/qtbridge-csharp) and Qt's
licensing terms for your case.

## Requirements

- Qt Bridge for C# (`QtGroup.Qt.Bridge.CSharp.<rid>`) and the Qt 6 version it
  requires.
- CMake, Ninja and a C++ toolchain.
- Windows x64, Linux x64 or macOS arm64.
