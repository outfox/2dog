---
title: WinUI 3 Configuration
description: "MSBuild settings for WinUI 3 hosts: desktop packs, self-contained Windows App SDK deployment, and Native AOT."
tagline: "Learn how to configure WinUI 3 hosts, from game packs to Native AOT."
---

# WinUI 3 Configuration

Set properties in `MyGame.winui.csproj`, which references `2dog.engine` and
`Microsoft.WindowsAppSDK`. Build and publish on Windows.
[Global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant;
[desktop publishing](/configuration#desktop-publishing) covers the pack,
self-contained, Native AOT and ReadyToRun properties.

The generated host is a `win-x64`, unpackaged and
[self-contained](https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
Windows App SDK app.

```bash
dotnet publish MyGame.winui -c Release -p:PublishAot=true
```

See the [WinUI host guide](/hosts/winui) for window embedding and UI behavior.
