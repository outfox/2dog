---
title: Avalonia Configuration
description: "MSBuild settings for Avalonia hosts: shared engine settings, desktop pack export, and Native AOT."
tagline: "Learn how to configure Godot in your Avalonia app, up to Native AOT."
---

# Avalonia Configuration

Set properties in `MyGame.avalonia.csproj`. Reference `2dog.avalonia` and
explicitly select `2dog.engine` or `2dog.engine.double`.
[Global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.
[Desktop publishing](/configuration#desktop-publishing) covers the pack,
self-contained, Native AOT and ReadyToRun properties.

```bash
dotnet publish MyGame.avalonia -c Release -r win-x64 -p:PublishAot=true
```

Keep the generated app manifest for correct DPI scaling.

See the [Avalonia host guide](/hosts/avalonia) for UI setup and platform requirements.
