---
title: WinForms
description: "Embed Godot in a Windows Forms window."
tagline: "Learn how to embed Godot in a Windows Forms app."
---

# WinForms

The WinForms host embeds Godot in a Windows Forms window with a Pause/Resume
button. It runs on Windows.

![Illus. Diego Rago: a white anthro dog in a hacker hoodie and glasses walking their blue godot robot dog](../img/2dog-harmony.png)

Godot reports `Engine.is_embedded_in_editor()` as `true` for this host;
account for this in scripts that check editor embedding.

## Use It

From your Godot project directory:

```bash
dnx 2dog add --winforms
dotnet run --project MyGame.winforms
```

## Customize It

Edit the generated form to add controls. Its UI thread runs Godot frames, so
event handlers can access the scene tree through `_engine.Tree`.
See the [showcase host](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.winforms).

Controls cannot overlap the game window. Use [Avalonia](./avalonia) or
[Qt Quick](./qt) for overlays or a cross-platform UI.

See [WinForms Configuration](/configuration/winforms) for build and publish settings.
