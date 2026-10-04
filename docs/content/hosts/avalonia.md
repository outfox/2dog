---
title: Avalonia
description: "Embed Godot in a cross-platform Avalonia window with controls over the game."
---

# Avalonia

The Avalonia host displays Godot inside a cross-platform desktop window using
`2dog.avalonia`. Avalonia controls can overlap the game, including translucent
controls.

## Use It

From your Godot project directory:

```bash
dnx 2dog add --avalonia
dotnet run --project MyGame.avalonia
```

## Customize It

A `GodotSession` owns the engine and runs frames on the UI thread.
`GodotControl` displays the viewport and forwards input. UI event handlers can
access game state through `session.Engine.Tree`.

See the [showcase host](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.avalonia)
for a pause button, time-scale slider, and FPS display.

## Publish It

Publish like the [generic host](./generic#publishing), with or without
[Native AOT](./generic#native-aot).
See [Avalonia Configuration](/configuration/avalonia) for build and publish settings.

## Requirements

- Avalonia 12.1 or later on Windows, Linux, or macOS.
- Keep the generated Windows app manifest for correct DPI scaling.
- IME composition and captured mouse input are not supported yet.
