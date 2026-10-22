---
title: WinUI 3
description: "Embed Godot in a Windows App SDK window."
tagline: "Learn how to embed Godot in a WinUI 3 app."
---

# WinUI 3

The WinUI 3 host embeds Godot in a Windows App SDK window with a Pause/Resume
button. It builds and runs on Windows.

![Illus. Diego Rago: a white anthro dog in a hacker hoodie and glasses walking their blue godot robot dog](../img/2dog-harmony.png)
*(get cozy and embed Godot in a real UI framework)*

Godot reports `Engine.is_embedded_in_editor()` as `true` for this host. If a
script checks this value, make sure that it also works in this host.

## Use It

On Windows, run these commands from your Godot project directory:

```bash
dnx 2dog add --winui
dotnet run --project MyGame.winui
```

## Customize It

Edit the generated host to add controls. Its UI thread runs Godot frames, so
event handlers can access the scene tree through `_engine.Tree`.
See the [showcase host](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.winui)
for an example.

The app runs without an MSIX package. WinUI controls cannot overlap the game
window. Use [Avalonia](./avalonia) or [Qt Quick](./qt) for overlays or a
cross-platform UI.

## Publish It

```bash
dotnet publish MyGame.winui -c Release
```

See [WinUI 3 Configuration](/configuration/winui) for Native AOT and other
publish settings.
