---
title: Web Configuration
description: "MSBuild properties for browser-wasm hosts: native variants, game packs, symbols, compression, and Mono AOT."
tagline: "Learn how to tune your browser build: packs, compression and AOT."
---

# Web Configuration

Set properties in `MyGame.web.csproj`, which references `2dog.engine` and
`2dog.browser-wasm`. The generated host uses `RuntimeIdentifier=browser-wasm`
and defaults to Release; pass `-c Debug` for a Debug build.
See [global settings](/configuration#properties) for the game directory and import controls.

## Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogWebVariant` | Follows the [configuration](/build-configurations) | Native Web engine: `debug` or `release` |
| `TwoDogExportPack` | `true` | Export game content; `false` uses an existing `wwwroot/godot.pck` |
| `TwoDogWebExportPreset` | `Web` | Preset in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack name |
| `TwoDogWebSizeManifest` | `true` | Write `twodog.sizes.json` for loading progress |
| `TwoDogWebStripMaps` | `true` with the release engine | Remove JavaScript source maps |
| `TwoDogWebPrecompress` | `true` with the dotnet CLI | Write Brotli (`.br`) and gzip (`.gz`) copies of large bundle files |
| `TwoDogWebPrecompressLevel` | `Optimal` | `Optimal` (balance speed and size), `Fastest`, `SmallestSize` (slower), or `NoCompression`; case-insensitive |
| `TwoDogWebSideModuleExports` | `true` | Export native symbols used by GDExtensions |
| `WasmEnableThreads` | `false` | Link the [multithreaded](/hosts/web#multithreading) runtime and engine (experimental) |
| `TwoDogWebSideModuleTableSlots` | `16384` | With threads: function table slots reserved for GDExtension side modules |
| `WasmEmitSymbolMap` | `false` in Release, `true` otherwise | Include native symbols for stack traces; explicit settings override the default |
| `WasmInitialHeapSize` | `256MB` | Initial memory allocation; memory can grow |

`2dog.browser-wasm` restores both debug and release natives. A missing selected
payload fails the build.

Changing `TwoDogWebPackName` also requires matching the pack URL in your boot page.

## Precompression

The dotnet CLI writes compressed copies next to the originals; set
`TwoDogWebPrecompress=false` to skip them. Visual Studio's MSBuild skips this
step.

## Trimming and AOT

See [Trimming](/configuration#trimming) for assemblies accessed only through
reflection.

Browser hosts run on Mono. Enable Mono's
[AOT compiler](https://learn.microsoft.com/aspnet/core/blazor/webassembly-build-tools-and-aot)
in the host's csproj instead of `PublishAot`; it applies to `dotnet publish`:

```xml
<PropertyGroup>
  <RunAOTCompilation>true</RunAOTCompilation>
</PropertyGroup>
```

`-p:RunAOTCompilation=true` sets it for a single publish.

See the [Web host guide](/hosts/web) for serving and deploying `AppBundle/`.
