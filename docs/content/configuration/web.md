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

| Property                        | Default                                            | Purpose                                                                                                      |
|---------------------------------|----------------------------------------------------|--------------------------------------------------------------------------------------------------------------|
| `TwoDogWebVariant`              | Follows the [configuration](/build-configurations) | Native engine variant: `debug` or `release`                                                                  |
| `TwoDogExportPack`              | `true`                                             | Export game content; `false` uses an existing `wwwroot/godot.pck`                                            |
| `TwoDogWebExportPreset`         | `Web`                                              | Preset in `export_presets.cfg`                                                                               |
| `TwoDogWebPackName`             | `godot.pck`                                        | Deployed pack name                                                                                           |
| `TwoDogWebSizeManifest`         | `true`                                             | Write `twodog.sizes.json` for loading progress                                                               |
| `TwoDogWebStripMaps`            | `true` with the release engine                     | Remove JavaScript source maps                                                                                |
| `TwoDogWebPrecompress`          | `true` with the dotnet CLI                         | Write Brotli (`.br`) and gzip (`.gz`) copies of large bundle files                                           |
| `TwoDogWebPrecompressLevel`     | `Optimal`                                          | `Optimal` (balance speed and size), `Fastest`, `SmallestSize` (slower), or `NoCompression`; case-insensitive |
| `TwoDogWebSideModuleExports`    | `true`                                             | Export native symbols used by GDExtensions                                                                   |
| `WasmEnableThreads`             | `false`                                            | Link the [multithreaded](/hosts/web#multithreading) runtime and engine (experimental)                        |
| `TwoDogWebSideModuleTableSlots` | `16384`                                            | With threads: function table slots reserved for GDExtension side modules                                     |
| `WasmEmitSymbolMap`             | `false` in Release, `true` otherwise               | Include native symbols for stack traces; explicit settings override the default                              |
| `WasmInitialHeapSize`           | `256MB`                                            | Initial memory allocation; memory can grow                                                                   |
| `TwoDogWebGpuCachePath`         | System temporary directory                         | Writable Emscripten port cache for WebGPU                                                                    |

`2dog.browser-wasm` restores both debug and release natives. If the payload
for the selected variant is missing, the build fails.

If you change `TwoDogWebPackName`, change the pack URL in your boot page to
match.

## Experimental WebGPU

Single-precision, single-threaded browser packages include WebGPU and WebGL.
During bundling, `renderer/rendering_method.web="mobile"` in `project.godot`
selects WebGPU when the native payload includes it. Other settings select WebGL.
The showcase uses Mobile. A new template project uses Compatibility by default.
To request WebGPU explicitly, configure `GODOT_CONFIG` in `wwwroot/index.html`
before you create `Engine`:

```javascript
'renderingDriver': 'webgpu',
'args': ['--rendering-driver', 'webgpu', '--rendering-method', 'mobile'],
```

The shell requests a device before it initializes Godot. If one of these
conditions occurs, the shell logs a warning and starts with `opengl3` and
`gl_compatibility`, with the same runtime and game pack:

- WebGPU is unavailable.
- No adapter is available.
- The adapter or device request fails.

This fallback happens before startup; it does not recover from later device loss
or native renderer failures. Game content must also work with Compatibility.

To always use Compatibility, set `renderer/rendering_method.web` to
`"gl_compatibility"`, or override both command-line options:

```javascript
'renderingDriver': 'opengl3',
'args': ['--rendering-driver', 'opengl3', '--rendering-method', 'gl_compatibility'],
```

The `opengl3` driver uses WebGL 2 in browsers (OpenGL ES 3.0), not WebGL 3.
WebGPU requires a secure context and browser support. Threaded and
double-precision payloads reject explicit WebGPU requests before device
creation. The package supplies the Emscripten port, link flags and writable
cache.

See [build configurations](/build-configurations#experimental-webgpu) for limits.

## Precompression

The dotnet CLI writes compressed copies next to the originals. To skip them,
set `TwoDogWebPrecompress=false`. Visual Studio's MSBuild skips this
step.

## Trimming and AOT

See [Trimming](/configuration#trimming) for assemblies accessed only through
reflection.

Browser hosts run on Mono. Enable Mono's
[AOT compiler](https://learn.microsoft.com/aspnet/core/blazor/webassembly-build-tools-and-aot)
in the host's csproj instead of `PublishAot`. This setting applies to
`dotnet publish`:

```xml
<PropertyGroup>
  <RunAOTCompilation>true</RunAOTCompilation>
</PropertyGroup>
```

`-p:RunAOTCompilation=true` sets it for a single publish.

See the [Web host guide](/hosts/web) for serving and deploying `AppBundle/`.
