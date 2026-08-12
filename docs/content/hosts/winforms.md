---
title: WinForms Host
description: "A Windows-only host that embeds the Godot engine inside a WinForms window, with a pause button driving the instance - how to add it and how it works."
---

# WinForms Host

The WinForms host embeds the engine inside a WinForms window and drives it
from the form's UI thread. Its UI is deliberately minimal: a panel the game
renders into and a working Pause/Resume button, ready to be extended with your
own controls.

## Use It

The host is opt-in and runs only on Windows:

```bash
dnx 2dog add --winforms
dotnet new 2dog -n MyGame --winforms true  # new project
dotnet run --project MyGame.winforms
```

The repository's own instance is
[`demos/showcase/showcase.winforms`](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.winforms).

## Capabilities

- Embeds Godot in a native WinForms window.
- Interleaves Godot frames and WinForms events on one STA UI thread.
- Lets event handlers safely access the scene tree.
- Builds on non-Windows systems with `EnableWindowsTargeting`, although it
  cannot run there.

## How It Works

Godot 4.7 supports embedding out of the box via the `--wid <window_id>`
argument – the same mechanism the Godot editor uses for its embedded game
window. Given a native window handle, the engine creates its main window as a
borderless popup owned by that handle instead of a regular top-level window.
Because 2dog hosts pass arguments to Godot verbatim, no engine or API changes
are involved:

```csharp
_engine = new Engine("MyGame", args:
[
    "--wid", Handle.ToInt64().ToString(CultureInfo.InvariantCulture),
    "--resolution", $"{panel.ClientSize.Width}x{panel.ClientSize.Height}",
    "--position", "0,0",
]);
```

Three consequences shape the host code:

- **The host owns geometry.** The embedded popup lives in screen coordinates
  and Godot refuses `window_set_size` for it, so the form drives the window
  with raw `SetWindowPos` from `Resize` and `LocationChanged` handlers –
  exactly how the editor drives its embedded game window.
- **The host owns the frame loop.** A classic WinForms game loop pumps
  `Iteration()` from `Application.Idle` whenever the message queue is empty,
  so Godot frames and UI events interleave on one STA thread, and WinForms
  event handlers can touch the scene tree directly.
- **Teardown happens before the owner window dies.** The form disposes the
  instance and engine in `OnFormClosing`, while its own handle still exists.

The Pause button pauses and resumes the running instance via
`GodotInstance.Pause()`/`Resume()` straight from its click handler - the UI
thread is the pump thread, so game state (including the scene tree via
`engine.Tree`) is safe to touch from event handlers.

## Limitations

- Windows only. For a cross-platform GUI host, use the
  [Avalonia host](/hosts/avalonia).
- The embedded window always draws above the form's client area, so WinForms
  controls cannot overlap the game rectangle - the
  [Avalonia host](/hosts/avalonia) composites instead, which lifts this
  restriction.
- `--wid` marks the instance as embedded (`Engine.is_embedded_in_editor()`
  returns `true` to scripts).
