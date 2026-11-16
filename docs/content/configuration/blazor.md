---
title: Blazor Configuration
description: "MSBuild configuration for the Blazor WebAssembly client: game packs, native symbols, memory, and SDK-managed compression."
tagline: "Learn how to configure Godot in Blazor, from game packs to compression."
---

# Blazor Configuration

Set 2dog properties in `MyGame.blazor/Client/MyGame.blazor.Client.csproj`.
The client references `2dog.engine`, `2dog.blazor`, and `2dog.browser-wasm`.
It normally sets `GodotProjectDir=../..`; see the
[global settings](/configuration#properties).

## Client Properties

| Property                     | Default                                            | Purpose                                                                                                |
|------------------------------|----------------------------------------------------|--------------------------------------------------------------------------------------------------------|
| `TwoDogWebVariant`           | Follows the [configuration](/build-configurations) | Native engine variant: `debug` or `release`                                                            |
| `TwoDogExportPack`           | `true`                                             | Export the game pack as a static web asset; `false` uses an existing `wwwroot/godot.pck` in the client |
| `TwoDogWebExportPreset`      | `Web`                                              | Preset in `export_presets.cfg`                                                                         |
| `TwoDogWebPackName`          | `godot.pck`                                        | Exported pack name; if you change it, set the same name in `GodotView.PackUrl`                         |
| `TwoDogWebSideModuleExports` | `true`                                             | Export native symbols used by GDExtensions                                                             |
| `WasmEmitSymbolMap`          | `false` in Release, `true` otherwise               | Include native symbols for stack traces; explicit settings override the default                        |
| `WasmInitialHeapSize`        | `256MB`                                            | Initial memory allocation; memory can grow                                                             |

`2dog.browser-wasm` restores both supported native variants. If the payload
for the selected variant is missing, the build fails. The client SDK manages its own bundle, progress,
source maps, and compression. `TwoDogWebSizeManifest`, `TwoDogWebStripMaps`,
and `TwoDogWebPrecompress` apply only to Web and WebXR hosts.

## Publishing, Trimming, and AOT

Publish the server project to include its client:

```bash
dotnet publish MyGame.blazor -c Release -p:RunAOTCompilation=true
```

`RunAOTCompilation` enables Mono's browser AOT compiler. See
[Trimming](/configuration#trimming) for assemblies accessed only through
reflection.

Keep the attributes on the client's generated `2dog.engine` and browser
package references. See the [Blazor host guide](/hosts/blazor) for `GodotView`.

## Experimental WebGPU

See [New Tricks: WebGPU](/configuration/new-tricks/webgpu#blazor) for
`GodotView` renderer settings, browser requirements, and Compatibility fallback.
See [Forward+ and Mobile on the web](/configuration/new-tricks/web-renderers)
to select the web
renderer and bake shaders before export.
