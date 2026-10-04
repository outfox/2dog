---
title: WinUI 3 Configuration
description: "MSBuild settings for WinUI 3 hosts: desktop packs, self-contained Windows App SDK deployment, and Native AOT."
---

# WinUI 3 Configuration

Set properties in `MyGame.winui.csproj`, which references `2dog.engine` and
`Microsoft.WindowsAppSDK`. Build and publish on Windows.
[Global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogExportPack` | `true` | Export content to an executable-adjacent `.pck`; `false` skips export, so supply game content separately |
| `TwoDogDesktopExportPreset` | `Windows Desktop` | Preset in `export_presets.cfg` for the Windows game pack |
| `PublishSelfContained` | `true` in the generated host | Bundle the .NET runtime; `false` requires an installed runtime |
| `PublishAot` | `false` | Compile the host and managed game code into a native executable |

The generated host sets `RuntimeIdentifier=win-x64`, `WindowsPackageType=None`,
and `WindowsAppSDKSelfContained=true` for an unpackaged app carrying its Windows
App SDK runtime. That runtime setting is separate from .NET's
`PublishSelfContained`.

```bash
dotnet publish MyGame.winui -c Release -p:PublishAot=true
```

Native AOT supports debug and release variants; see
[requirements and trimmer roots](/configuration#native-aot).
The host uses `OutputType=WinExe`; choose `Exe` to see console diagnostics.

See the [WinUI host guide](/hosts/winui) for window embedding and UI behavior.
