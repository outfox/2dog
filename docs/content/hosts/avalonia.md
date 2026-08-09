---
title: Avalonia Host
description: "A cross-platform host that composites the Godot viewport inside an Avalonia window via the 2dog.avalonia package - Avalonia controls render on top of the running game."
---

# Avalonia Host

The Avalonia host shows the engine's viewport inside an
[Avalonia](https://avaloniaui.net) window through the reusable `GodotControl`
from the `2dog.avalonia` package. Unlike native window embedding (the
[WinForms host](/hosts/winforms)), the game enters Avalonia's compositor as
an ordinary control - **Avalonia controls placed over it render on top of the
running game**, translucency included.

It pulls the Avalonia UI framework and the `2dog.avalonia` package into your
project, so it is never part of the default host set - add it explicitly:

```bash
2dog add --avalonia                        # existing project
dotnet new 2dog -n MyGame --avalonia true  # new project
```

Then run it:

```bash
dotnet run --project MyGame.avalonia
```

The repository's own instance is
[`demos/showcase/showcase.avalonia`](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.avalonia),
which floats a translucent panel with a Pause button, a time-scale slider and
an FPS readout over the showcase scene.

## How It Works

A `GodotSession` owns the engine (one per process) and pumps `Iteration()` on
the Avalonia UI thread, paced by the compositor's animation frames. The
`GodotControl` is just a view: it can be attached, detached and re-parented
freely while the session keeps running.

```csharp
var session = new GodotSession(new GodotSessionOptions
{
    Project = "MyGame",
    ExtraArgs = args,   // forwarded to Godot verbatim (--verbose, --quit-after, ...)
});
godotControl.Session = session;
session.Start();
```

- **Presentation.** Each frame, the control presents the engine's main
  viewport into the Avalonia scene. On Windows this is **zero-copy**: the
  engine copies the viewport into a D3D11 keyed-mutex shared texture on the
  GPU, and Avalonia's compositor imports it directly via
  `ICompositionGpuInterop`. Elsewhere (and on natives without texture
  sharing) a CPU readback fallback runs instead
  (`RenderingServer.Texture2DGet` into a `WriteableBitmap`).
  `GodotSessionOptions.PresentationMode` selects; `Auto` (the default) picks
  the best available path.
- **Input.** The engine's own window never sees the pointer: the control
  translates Avalonia pointer, wheel and keyboard events into Godot input
  events and injects them with `Input.ParseInputEvent`, DPI-scaled into
  viewport coordinates. The engine's requested cursor shape is reflected back
  onto the control.
- **Lifecycle.** `Iteration()` returning true (the game asked to quit) raises
  `GodotSession.QuitRequested`; the host closes its window and disposes the
  session in `OnClosing`, before the process exits.
- The UI thread is the pump thread, so event handlers can touch game state
  directly, including the scene tree via `session.Engine.Tree`.

## Limitations

- Zero-copy GPU presentation is Windows-only so far; Linux and macOS use the
  CPU readback path (a GPU→CPU→GPU copy per frame). The natives already
  export shareable textures on all three platforms (Vulkan opaque fds,
  IOSurfaces), so the remaining work is host-side.
- Full IME composition and `Input.MouseMode.Captured` (relative/locked mouse)
  are not supported yet.
- Requires Avalonia 11.3.x; an application on Avalonia 12 cannot load the
  current `2dog.avalonia` build.
