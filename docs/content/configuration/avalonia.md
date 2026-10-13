---
title: Avalonia Configuration
description: "MSBuild settings for Avalonia hosts: shared engine settings, desktop pack export, and Native AOT."
---

# Avalonia Configuration

Set properties in `MyGame.avalonia.csproj`. Reference `2dog.avalonia` and
explicitly select `2dog.engine` or `2dog.engine.double`.
[Global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogExportPack` | `true` | Export content to an executable-adjacent `.pck`; `false` skips export, so supply game content separately |
| `TwoDogDesktopExportPreset` | RID-mapped | `Windows Desktop`, `Linux`, or `macOS`, according to the publish target |
| `PublishSelfContained` | `true` in the generated host | Bundle the .NET runtime; `false` requires an installed runtime |
| `PublishAot` | `false` | Compile the host and managed game code into a native executable |

```bash
dotnet publish MyGame.avalonia -c Release -r win-x64 -p:PublishAot=true
```

Native AOT supports debug and release variants; see
[requirements and trimmer roots](/configuration#native-aot).
The generated host uses `OutputType=WinExe`; choose `Exe` to see the engine's
console diagnostics on Windows. Keep its app manifest for correct DPI scaling.

See the [Avalonia host guide](/hosts/avalonia) for UI setup and platform requirements.
