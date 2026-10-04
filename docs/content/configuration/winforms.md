---
title: WinForms Configuration
description: "MSBuild settings for WinForms hosts: Windows targeting, pack export, runtime bundling, and trimming limitations."
---

# WinForms Configuration

Set properties in `MyGame.winforms.csproj`, which references `2dog.engine`.
The generated host targets `net10.0-windows` with `UseWindowsForms=true`.
[Global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogExportPack` | `true` | Export content to an executable-adjacent `.pck`; `false` skips export, so supply game content separately |
| `TwoDogDesktopExportPreset` | `Windows Desktop` | Preset in `export_presets.cfg` for the Windows game pack |
| `PublishSelfContained` | `true` in the generated host | Bundle the .NET runtime; `false` requires an installed runtime |

```bash
dotnet publish MyGame.winforms -c Release -r win-x64
```

WinForms does not support trimming or Native AOT. Keep the generated
`ApplicationHighDpiMode=SystemAware` and app manifest so WinForms and Godot use
the same DPI mode. The host uses `OutputType=WinExe`; choose `Exe` to see
console diagnostics.

See the [WinForms host guide](/hosts/winforms) for window embedding and UI behavior.
