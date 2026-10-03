---
title: WinUI 3
description: "Embed Godot in a Windows App SDK window."
---

# WinUI 3

The WinUI 3 host embeds Godot in a Windows App SDK window with a Pause/Resume
button. It builds and runs on Windows.

Godot reports `Engine.is_embedded_in_editor()` as `true` for this host;
account for this in scripts that check editor embedding.

## Use It

From your Godot project directory on Windows:

```bash
dnx 2dog add --winui
dotnet run --project MyGame.winui
```

## Customize It

Edit the generated host to add controls. Its UI thread runs Godot frames, so
event handlers can access the scene tree through `_engine.Tree`.
See the [showcase host](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.winui).

The app runs unpackaged. WinUI controls cannot overlap the game window;
use [Avalonia](./avalonia) for overlays or a cross-platform UI.

## Publish It

Publish like the [generic host](./generic#publishing), with or without
[Native AOT](./generic#native-aot).
