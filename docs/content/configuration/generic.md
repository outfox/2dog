---
title: Generic .NET Configuration
description: "MSBuild settings for the generic .NET host: console behavior, desktop pack export, and Native AOT."
tagline: "Learn how to configure your console host, from pack export to Native AOT."
---

# Generic .NET Configuration

Set properties in `MyGame.2dog.csproj`, which references `2dog.engine`.
The [global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant;
[desktop publishing](/configuration#desktop-publishing) covers the pack,
self-contained, Native AOT and ReadyToRun properties.

```bash
dotnet publish MyGame.2dog -c Release -r win-x64 -p:PublishAot=true
```

See the [generic host guide](/hosts/generic) for running the host and passing
Godot arguments.
