---
title: Blazor Configuration
description: "MSBuild configuration for the Blazor WebAssembly client: game packs, native symbols, memory, and SDK-managed compression."
---

# Blazor Configuration

Set 2dog properties in `MyGame.blazor/Client/MyGame.blazor.Client.csproj`.
The client references `2dog.engine`, `2dog.blazor`, and `2dog.browser-wasm`.
It normally sets `GodotProjectDir=../..`; see the
[global settings](/configuration#properties).

## Client Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogWebVariant` | `debug` in Debug, `editor` in Editor, `release` otherwise | Native Web engine; explicit settings override the default. The `editor` variant is unsupported |
| `TwoDogExportPack` | `true` | Export the game pack as a static web asset; `false` uses an existing `wwwroot/godot.pck` in the client |
| `TwoDogWebExportPreset` | `Web` | Preset in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Exported pack name; match it in `GodotView.PackUrl` if changed |
| `TwoDogWebSideModuleExports` | `true` | Export native symbols used by GDExtensions |
| `WasmEmitSymbolMap` | `false` in Release, `true` otherwise | Include native symbols for stack traces; explicit settings override the default |
| `WasmInitialHeapSize` | `256MB` | Initial memory allocation; memory can grow |

`2dog.browser-wasm` restores both supported native variants; a missing selected
payload fails the build. The client SDK manages its own bundle, progress,
source maps, and compression. `TwoDogWebSizeManifest`, `TwoDogWebStripMaps`,
and `TwoDogWebPrecompress` are settings for Web/WebXR hosts.

## Publishing, Trimming, and AOT

Publish the server project to include its client:

```bash
dotnet publish MyGame.blazor -c Release -p:RunAOTCompilation=true
```

`RunAOTCompilation` enables Mono's browser AOT compiler. The generated client
preserves the game, host, `GodotSharp`, and `twodog` assemblies. Add
`<TrimmerRootAssembly Include="MyLibrary"/>` in an `<ItemGroup>` for other
assemblies accessed only through reflection.

Keep `PrivateAssets="all"` on the client's engine and browser package references
so their build targets stay out of the server; the generated engine reference
also sets `Publish="true"` so its managed assemblies remain in the client publish.
See the [Blazor host guide](/hosts/blazor) for `GodotView` and static asset serving.
