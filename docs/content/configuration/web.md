---
title: Web Configuration
description: "MSBuild properties for browser-wasm hosts: native variants, game packs, symbols, compression, and Mono AOT."
---

# Web Configuration

Set properties in `MyGame.web.csproj`, which references `2dog.engine` and
`2dog.browser-wasm`. The generated host uses `RuntimeIdentifier=browser-wasm`
and defaults to Release; pass `-c Debug` for a Debug build.
See [global settings](/configuration#properties) for the game directory and import controls.

## Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogWebVariant` | `debug` in Debug, `editor` in Editor, `release` otherwise | Native Web engine; explicit settings override the default. The `editor` variant is unsupported |
| `TwoDogExportPack` | `true` | Export game content; `false` uses an existing `wwwroot/godot.pck` |
| `TwoDogWebExportPreset` | `Web` | Preset in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack name |
| `TwoDogWebSizeManifest` | `true` | Write `twodog.sizes.json` for loading progress |
| `TwoDogWebStripMaps` | `true` with the release engine | Remove JavaScript source maps |
| `TwoDogWebPrecompress` | `true` with `dotnet publish` | Write Brotli (`.br`) and gzip (`.gz`) copies of large bundle files |
| `TwoDogWebPrecompressLevel` | `Optimal` | `Optimal` (balance speed and size), `Fastest`, `SmallestSize` (slower), or `NoCompression`; case-insensitive |
| `TwoDogWebSideModuleExports` | `true` | Export native symbols used by GDExtensions |
| `WasmEmitSymbolMap` | `false` in Release, `true` otherwise | Include native symbols for stack traces; explicit settings override the default |
| `WasmInitialHeapSize` | `256MB` | Initial memory allocation; memory can grow |

`2dog.browser-wasm` restores both debug and release natives. A missing selected
payload fails the build. Editor configuration selects the unsupported editor
variant and fails; use Debug or Release, or explicitly select a supported variant.

Changing `TwoDogWebPackName` also requires matching the pack URL in your boot page.

## Precompression

`dotnet publish` enables precompression; set `TwoDogWebPrecompress=false` to
skip it. Originals are kept. Currently, Full and Mono MSBuild skip this step even when
explicitly enabled; use `dotnet publish` to generate compressed copies.
See [MSBuild runtimes](/configuration#msbuild-runtimes).

`NoCompression` still creates `.br` and `.gz` files. Use
`TwoDogWebPrecompress=false` to skip generating them.

## Trimming and AOT

The generated host preserves the game, host, `GodotSharp`, and `twodog`
assemblies. Root other assemblies accessed only through reflection:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MyLibrary"/>
</ItemGroup>
```

Browser hosts run on Mono. Enable Mono's AOT compiler with
`-p:RunAOTCompilation=true`; use this instead of `PublishAot`.

See the [Web host guide](/hosts/web) for serving and deploying `AppBundle/`.
