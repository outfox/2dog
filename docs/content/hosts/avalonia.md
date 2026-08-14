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

The repository's own instance is
[`demos/showcase/showcase.avalonia`](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.avalonia),
which floats a translucent panel with a Pause button, a time-scale slider and
an FPS readout over the showcase scene.

## Use It

The host is opt-in because it adds Avalonia and `2dog.avalonia` dependencies:

```bash
dnx 2dog add --avalonia
dotnet new 2dog -n MyGame --avalonia true
dotnet run --project MyGame.avalonia
```

## Capabilities

- Runs on Windows, Linux, and macOS.
- Lets Avalonia controls overlap the game, including translucent controls.
- Maps Avalonia pointer, wheel, and keyboard events into Godot.
- Exposes the scene tree to UI-thread event handlers.

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
  viewport into the Avalonia scene. The primary path is **zero-copy**: the
  engine copies the viewport into a shared GPU texture that Avalonia's
  compositor imports directly (a D3D11 keyed-mutex texture on Windows -
  shared via an NT handle where the driver supports importing one, falling
  back to a legacy KMT global shared handle - exported Vulkan memory on
  Linux, an IOSurface on macOS). Where the
  compositor cannot import the texture, a CPU readback fallback runs instead.
  `GodotSessionOptions.PresentationMode` selects the path; `Auto` (the
  default) picks the best one available. Shared textures cannot cross GPU
  adapters, so on Windows the session steers the engine onto the compositor's
  adapter (Godot's `--gpu-luid`) - on hybrid-GPU laptops this means the
  integrated GPU. Pass your own `--gpu-index`/`--gpu-luid` in `ExtraArgs` to
  override.
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

- Zero-copy presentation is implemented for all three desktop platforms but
  verified end-to-end on Windows only so far; on Linux and macOS, `Auto`
  falls back to the CPU readback path automatically wherever the compositor
  cannot import the shared texture.
- Full IME composition and `Input.MouseMode.Captured` (relative/locked mouse)
  are not supported yet.
- Requires Avalonia 12.1+. On Windows, keep the scaffolded host's app-manifest
  DPI declaration: without it the engine's own DPI-awareness call lands mid-run
  and desyncs Avalonia's scaling (content renders oversized and clipped).
