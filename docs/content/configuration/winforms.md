---
title: WinForms Configuration
description: "MSBuild settings for WinForms hosts: Windows targeting, pack export, runtime bundling, and trimming limitations."
tagline: "Learn how to configure Godot in your Windows Forms app."
---

# WinForms Configuration

Set properties in `MyGame.winforms.csproj`, which references `2dog.engine`.
The generated host targets `net10.0-windows` with `UseWindowsForms=true`.
[Global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant;
[desktop publishing](/configuration#desktop-publishing) covers the pack,
self-contained and ReadyToRun properties.

```bash
dotnet publish MyGame.winforms -c Release -r win-x64
```

WinForms does not support trimming or Native AOT; ReadyToRun works. Keep the
generated `ApplicationHighDpiMode=SystemAware` and app manifest so WinForms and
Godot use the same DPI mode.

See the [WinForms host guide](/hosts/winforms) for window embedding and UI behavior.
