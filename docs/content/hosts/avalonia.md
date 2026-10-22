---
title: Avalonia
description: "Embed Godot in a cross-platform Avalonia window with controls over the game."
tagline: "Learn how to embed Godot in an Avalonia app, with your UI right on top."
---

# Avalonia

The Avalonia host uses `2dog.avalonia` to show Godot inside a cross-platform
desktop window. Avalonia controls can overlap the game, including translucent
controls.

![Illus. Diego Rago: a white anthro dog in a hacker hoodie and glasses walking their blue godot robot dog](../img/2dog-harmony.png)
*(get cozy and embed Godot in a real UI framework)*

## Use It

Run these commands from your Godot project directory:

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

```bash
dotnet publish MyGame.avalonia -c Release
```

See [Avalonia Configuration](/configuration/avalonia) for Native AOT and other
publish settings.

## Requirements

- Avalonia 12.1 or later on Windows, Linux, or macOS.
- IME composition and captured mouse input are not supported yet.
