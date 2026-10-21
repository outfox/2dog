# 2dog.qt

Embed a Godot viewport in a [Qt Quick](https://doc.qt.io/qt-6/qtquick-index.html) application built with
[Qt Bridge for C#](https://github.com/qt/qtbridge-csharp). The `GodotView` item from the `TwoDog` QML module
shows the engine inside the scene, so QML items render **on top of** the running game.

```csharp
using Qt.Quick;
using twodog;

static void Main(string[] args)
{
    using var session = new QtGodotSession("mygame", args: args);
    Qml.LoadFromRootModule("Main");
    Qml.WaitForExit();
}
```

```qml
import QtQuick
import TwoDog

Window {
    visible: true
    GodotView { anchors.fill: parent; focus: true }
}
```

- The **session** owns the engine (one per process). The engine starts once the first `GodotView` renders and is
  pumped on Qt's GUI thread, once per Qt Quick frame. `Started`, `FrameAdvanced`, `ActiveModeChanged` and `Stopped`
  run there, as do C# methods that QML calls, so both can use the Godot API directly. Qt Bridge runs `Main` on a
  different thread.
- Dispose the session after `Qml.WaitForExit()`: it waits until the engine has shut down on the GUI thread.
  When Godot quits, the application quits.
- On Windows, Qt Quick's default Direct3D 11 backend shares GPU textures with the engine (zero-copy). Other platforms
  and backends use CPU readback, which is also the automatic fallback; `PresentationMode` forces either and
  `ActiveMode` reports which one runs.
- While the window is minimized or hidden the engine keeps running at a background rate without rendering, as long
  as Qt's event loop runs. Pausing is the app's call: set `IsPaused`. One `GodotView` shows the engine at a time.

The `TwoDog` module ships as C++ source and builds against the Qt your app uses, with the toolchain Qt Bridge
already requires (CMake, Ninja, and MSVC on Windows). The build copies it to `<app>/TwoDog`. Qt comes from Qt
Bridge's `QtDir`: bundled in `QtGroup.Qt.Bridge.CSharp.win-x64`, and set by you on Linux and macOS.

Requires an explicit reference to `2dog.engine` or `2dog.engine.double`, and a
`QtGroup.Qt.Bridge.CSharp.<rid>` package.

2dog.qt contains no Qt code. Qt Bridge for C# and Qt are licensed under LGPL-3.0 or a commercial license; an app
that ships their binaries takes on those terms. See https://2dog.dev/hosts/qt#licensing.

Docs: https://2dog.dev/hosts/qt
