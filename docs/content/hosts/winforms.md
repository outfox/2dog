---
title: WinForms Host (Demo)
description: "A Windows-only repository demo that embeds the Godot engine inside a WinForms window - how it works and its limitations. A demo, not a template."
---

# WinForms Host (Demo)

The repository ships a Windows-only demo host,
[`demos/showcase/showcase.winforms`](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.winforms),
that embeds the engine inside a WinForms window. It is a repo demo, not a
[`2dog new`](/templates) template.

```bash
dotnet run --project demos/showcase/showcase.winforms
```

## How It Works

Godot 4.7 supports embedding out of the box via the `--wid <window_id>`
argument  –  the same mechanism the Godot editor uses for its embedded game
window. Given a native window handle, the engine creates its main window as a
borderless popup owned by that handle instead of a regular top-level window.
Because 2dog hosts pass arguments to Godot verbatim, no engine or API changes
are involved:

```csharp
_engine = new Engine("showcase", Engine.ResolveProjectDir(),
[
    "--wid", Handle.ToInt64().ToString(CultureInfo.InvariantCulture),
    "--resolution", $"{panel.ClientSize.Width}x{panel.ClientSize.Height}",
    "--position", "0,0",
]);
```

Three consequences shape the host code:

- **The host owns geometry.** The embedded popup lives in screen coordinates
  and Godot refuses `window_set_size` for it, so the form drives the window
  with raw `SetWindowPos` from `Resize` and `LocationChanged` handlers  – 
  exactly how the editor drives its embedded game window.
- **The host owns the frame loop.** A classic WinForms game loop pumps
  `Iteration()` from `Application.Idle` whenever the message queue is empty,
  so Godot frames and UI events interleave on one STA thread, and WinForms
  event handlers can touch the scene tree directly.
- **Teardown happens before the owner window dies.** The form disposes the
  instance and engine in `OnFormClosing`, while its own handle still exists.

The demo's buttons drive the running game from WinForms: one flips the
`SpinSpeed` of the showcase's `SpinningCube` scripts through their generated
C# types, the other pauses and resumes the instance via
`GodotInstance.Pause()`/`Resume()`.

## Limitations

- Windows only. The same `--wid` route works on X11, so an Avalonia host
  could cover Linux; macOS would need Godot's separate `embedded` display
  server.
- The embedded window always draws above the form's client area, so WinForms
  controls cannot overlap the game rectangle.
- `--wid` marks the instance as embedded (`Engine.is_embedded_in_editor()`
  returns `true` to scripts).
