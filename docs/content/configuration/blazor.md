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
| `TwoDogWebVariant`           | Follows the [configuration](/build-configurations) | Native Web engine: `debug` or `release`                                                                |
| `TwoDogExportPack`           | `true`                                             | Export the game pack as a static web asset; `false` uses an existing `wwwroot/godot.pck` in the client |
| `TwoDogWebExportPreset`      | `Web`                                              | Preset in `export_presets.cfg`                                                                         |
| `TwoDogWebPackName`          | `godot.pck`                                        | Exported pack name; match it in `GodotView.PackUrl` if changed                                         |
| `TwoDogWebSideModuleExports` | `true`                                             | Export native symbols used by GDExtensions                                                             |
| `WasmEmitSymbolMap`          | `false` in Release, `true` otherwise               | Include native symbols for stack traces; explicit settings override the default                        |
| `WasmInitialHeapSize`        | `256MB`                                            | Initial memory allocation; memory can grow                                                             |

`2dog.browser-wasm` restores both supported native variants; a missing selected
payload fails the build. The client SDK manages its own bundle, progress,
source maps, and compression. `TwoDogWebSizeManifest`, `TwoDogWebStripMaps`,
and `TwoDogWebPrecompress` are settings for Web/WebXR hosts.

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

Set `RenderingDriver="webgpu"` on `GodotView` to select WebGPU Mobile. The default
remains OpenGL/WebGL Compatibility. `--rendering-driver webgpu` in `Args` also
selects WebGPU when `RenderingDriver` is unset.

This requires the single-precision, single-threaded browser payload and WebGPU
browser support in a secure context. Unsupported payloads fail before device
creation. The device is reused across view lifetimes; cancelled initialization
releases an unclaimed device.

See [build configurations](/build-configurations#experimental-webgpu).
