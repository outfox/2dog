---
title: Generic .NET Configuration
description: "MSBuild settings for the generic .NET host: console behavior, desktop pack export, and Native AOT."
---

# Generic .NET Configuration

Set properties in `MyGame.2dog.csproj`, which references `2dog.engine`.
The [global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.

## Publishing

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogExportPack` | `true` | Export content to an executable-adjacent `.pck`; `false` skips export, so supply game content separately |
| `TwoDogDesktopExportPreset` | RID-mapped | Preset in `export_presets.cfg`: `Windows Desktop`, `Linux`, or `macOS`, according to the publish target |
| `PublishSelfContained` | `true` in the generated host | Bundle the .NET runtime; `false` requires an installed runtime |
| `PublishAot` | `false` | Compile the host and managed game code into a native executable |

```bash
dotnet publish MyGame.2dog -c Release -r win-x64 -p:PublishAot=true
```

Native AOT supports debug and release variants; see
[requirements and trimmer roots](/configuration#native-aot).
`PublishSingleFile` is unsupported for ordinary desktop publishing; use
Native AOT when you need a single native executable.

## Console Behavior

The generated host uses `OutputType=WinExe` in Release to hide the Windows
console, and `Exe` otherwise. Set `OutputType=Exe` to keep console output in
Release. Linux and macOS treat `WinExe` as `Exe`.

See the [generic host guide](/hosts/generic) for running the host and passing
Godot arguments.
