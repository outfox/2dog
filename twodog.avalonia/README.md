# 2dog.avalonia

Embed a Godot viewport inside an [Avalonia](https://avaloniaui.net) application.
`GodotControl` brings the engine's rendered output into Avalonia's compositor, so ordinary
Avalonia controls  –  panels, buttons, popups  –  render **on top of** the running game.

```csharp
var session = new GodotSession(new GodotSessionOptions
{
    Project = "mygame",
    Path = Engine.ResolveContent(),
});
godotControl.Session = session;
session.Start();
```

- The **session** owns the engine (one per process) and pumps frames on the Avalonia UI thread.
- The **control** is a view: it can be attached, detached, and re-parented freely.
- On natives with texture-sharing support the viewport is composited **zero-copy** on the GPU
  (D3D11 shared textures on Windows, Vulkan external memory on Linux, IOSurface on macOS);
  otherwise a CPU readback fallback is used automatically (`GodotSessionOptions.PresentationMode`).
- Dispose the session before the application exits (e.g. in `Window.OnClosing`).

Requires the `2dog.engine` package (pulled in automatically) and Avalonia 12.1+.
On Windows, declare DPI awareness in your app manifest (the scaffolded host does) so the
engine's own DPI call cannot desync Avalonia's scaling mid-run.

Docs: https://2dog.dev/hosts/avalonia
