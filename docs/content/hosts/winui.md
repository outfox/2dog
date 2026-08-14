---
title: WinUI 3 Host
description: "A Windows-only host that embeds the Godot engine inside a WinUI 3 (Windows App SDK) window and pumps frames from the XAML dispatcher - how to add it and how it works."
---

# WinUI 3 Host

The WinUI 3 host embeds the engine inside a Windows App SDK window and drives
it from the XAML UI thread. Like the [WinForms host](/hosts/winforms), its UI
is deliberately minimal: a panel the game renders into and a working
Pause/Resume button, ready to be extended with your own controls.

## Use It

The host is opt-in and Windows-only - unlike WinForms, it does not even build
on other systems, so the generated solution excludes it from plain solution
builds:

```bash
dnx 2dog add --winui
dotnet new 2dog -n MyGame --winui true  # new project
dotnet run --project MyGame.winui
```

The repository's own instance is
[`demos/showcase/showcase.winui`](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.winui).

## Capabilities

- Embeds Godot in a WinUI 3 window (Windows App SDK, unpackaged, self-contained -
  `dotnet run` works without MSIX packaging or the Windows App Runtime installer).
- Interleaves Godot frames and XAML events on one STA UI thread.
- Lets event handlers safely access the scene tree.
- Tracks per-monitor DPI: XAML lengths are DIPs, so the host scales the embed
  rectangle by the panel's `RasterizationScale`.

## How It Works

The embedding mechanism is the same `--wid <window_id>` argument the
[WinForms host](/hosts/winforms) uses (and the Godot editor's embedded game
window): given the WinUI window's HWND, the engine creates its main window as
a borderless popup owned by that handle, and the host drives its geometry with
`SetWindowPos` from `SizeChanged`, `AppWindow.Changed`, and `XamlRoot.Changed`
handlers.

Two things are WinUI-specific:

- **The host is a code-only WinUI 3 app.** With no XAML in the project there
  is no generated metadata provider, so the `Application` subclass forwards
  WinUI's own `XamlControlsXamlMetaDataProvider` itself and merges
  `XamlControlsResources` in `OnLaunched` - without those, the first control
  lookup dies in native code with a stowed exception (`0xC000027B`).
- **The frame pump is a self-reposting dispatcher callback.** WinUI has no
  idle event, so the host enqueues one `Iteration()` per pass at
  `DispatcherQueuePriority.Low`; normal-priority input and layout always
  interleave, and the UI thread stays the pump thread - event handlers can
  touch the scene tree (via `engine.Tree`) directly.

Teardown happens in `AppWindow.Closing`/`Closed`, while the owner HWND still
exists - Godot self-closes its window when the owner disappears, and the
engine must be gone before the process-exit libgodot unload runs.

## Limitations

- Windows only, at build time too: the Windows App SDK tooling has no
  cross-compilation story, which is why the solution carries the project with
  `<Build Project="false" />`. For a cross-platform GUI host, use the
  [Avalonia host](/hosts/avalonia).
- The embedded window always draws above the XAML content, so WinUI controls
  cannot overlap the game rectangle - the [Avalonia host](/hosts/avalonia)
  composites instead, which lifts this restriction.
- `--wid` marks the instance as embedded (`Engine.is_embedded_in_editor()`
  returns `true` to scripts).
