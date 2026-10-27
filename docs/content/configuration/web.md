---
title: Web Configuration
description: "MSBuild properties for browser-wasm hosts: native variants, game packs, symbols, compression, and Mono AOT."
tagline: "Learn how to tune your browser build: packs, compression and AOT."
keywords: "Godot C# web export, WebGPU, Mono AOT, GDExtension on web, browser-wasm"
---

# Web Configuration

Set properties in `MyGame.web.csproj`, which references `2dog.engine` and
`2dog.browser-wasm`. The generated host uses `RuntimeIdentifier=browser-wasm`
and defaults to Release; pass `-c Debug` for a Debug build.
See [global settings](/configuration#properties) for the game directory and import controls.

## Properties

| Property                            | Default                                            | Purpose                                                                                                          |
|-------------------------------------|----------------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| `TwoDogWebVariant`                  | Follows the [configuration](/build-configurations) | Native engine variant: `debug` or `release`                                                                      |
| `TwoDogExportPack`                  | `true`                                             | Export game content; `false` uses an existing `wwwroot/godot.pck`                                                |
| `TwoDogWebExportPreset`             | `Web`                                              | Preset in `export_presets.cfg`                                                                                   |
| `TwoDogWebPackName`                 | `godot.pck`                                        | Deployed pack name                                                                                               |
| `TwoDogWebSizeManifest`             | `true`                                             | Write `twodog.sizes.json` for loading progress                                                                   |
| `TwoDogWebStripMaps`                | `true` with the release engine                     | Remove JavaScript source maps                                                                                    |
| `TwoDogWebPrecompress`              | `true` with the dotnet CLI                         | Write Brotli (`.br`) and gzip (`.gz`) copies of large bundle files                                               |
| `TwoDogWebPrecompressLevel`         | `Optimal`                                          | `Optimal` (balance speed and size), `Fastest`, `SmallestSize` (slower), or `NoCompression`; case-insensitive     |
| `TwoDogWebPrecompressFormats`       | `br;gz`                                            | Compressed copies to write: `br`, `gz`, or `br;gz`                                                               |
| `TwoDogWebPrecompressKeepOriginals` | `true`                                             | `false` removes the uncompressed engine, pack and .NET binaries; needs `gz` ([upload limits](#web-hosting-with-upload-limits)) |
| `TwoDogWebSideModuleExports`        | `true`                                             | Export native symbols used by GDExtensions                                                                       |
| `WasmEnableThreads`                 | `false`                                            | Link the [multithreaded](/hosts/web#multithreading) runtime and engine (experimental)                            |
| `TwoDogWebSideModuleTableSlots`     | `16384`                                            | With threads: function table slots reserved for GDExtension side modules                                         |
| `WasmEmitSymbolMap`                 | `false` in Release, `true` otherwise               | Include native symbols for stack traces; explicit settings override the default                                  |
| `WasmInitialHeapSize`               | `256MB`                                            | Initial memory allocation; memory can grow                                                                       |
| `TwoDogWebGpuCachePath`             | System temporary directory                         | Writable Emscripten port cache for WebGPU                                                                        |

`2dog.browser-wasm` restores both debug and release natives. If the payload
for the selected variant is missing, the build fails.

If you change `TwoDogWebPackName`, change the pack URL in your boot page to
match.

## Experimental WebGPU

Single-precision, single-threaded browser packages include WebGPU and WebGL.
During bundling, `renderer/rendering_method.web="forward_plus"` or `"mobile"` in
`project.godot` selects WebGPU when the native payload includes it. Other
settings select WebGL. The showcase uses Forward+. A new template project uses
Compatibility by default. To request WebGPU explicitly, configure `GODOT_CONFIG`
in `wwwroot/index.html` before you create `Engine`:

```javascript
'renderingDriver': 'webgpu',
'args': ['--rendering-driver', 'webgpu', '--rendering-method', 'forward_plus'],
```

Adapters with fewer than 40 sampled textures or 10 storage buffers per shader
stage start Mobile instead, with one console warning. Adapters without
`texture-formats-tier2`, such as Firefox, run Forward+ without SDFGI and VoxelGI;
see [adapter and shader limits](/build-configurations#adapter-and-shader-limits).
Firefox 158 on Linux needs `dom.webgpu.enabled` in `about:config` before it
exposes WebGPU; without it, the shell starts Compatibility.

Without baking, the browser translates every Forward+ shader variant on its main thread at the first start, which can
take a minute and trigger the browser's unresponsive-page prompt. Enable `shader_baker/enabled` in the `Web` export
preset to ship the translated shaders in the pack. Publishing then runs the headless export with a window-less
rendering device (Vulkan, or Metal on macOS), so it needs a GPU or a software adapter such as lavapipe, but no
display. Baked shaders make the pack larger, by about 18 MB compressed for a small Forward+ project.

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

The dotnet CLI writes Brotli (`.br`) and gzip (`.gz`) copies next to the large
bundle files. Visual Studio's MSBuild skips this step. Choose the settings
that match your web server:

| Your server                                   | Settings                                                                                  |
|-----------------------------------------------|-------------------------------------------------------------------------------------------|
| Serves precompressed files                    | Defaults, or `TwoDogWebPrecompressFormats` set to the one format it serves (`br` or `gz`) |
| Compresses responses itself                   | `TwoDogWebPrecompress=false`                                                              |
| Compresses nothing                            | Defaults, plus [compression in the page](#compression-in-the-page)                        |
| Compresses nothing and limits the upload size | [gzip only](#web-hosting-with-upload-limits)                                              |

Servers that serve precompressed files include
[Caddy](https://caddyserver.com/docs/caddyfile/directives/file_server) with
`precompressed`, and nginx with
[`gzip_static`](https://nginx.org/en/docs/http/ngx_http_gzip_static_module.html)
or [`brotli_static`](https://github.com/google/ngx_brotli). They send a copy
in place of the original when the browser accepts its format, so keep the
originals for browsers that do not.
[dotnet-serve](https://github.com/natemcmaster/dotnet-serve) with `-b -z`
compresses responses itself and ignores the copies.

A publish removes copies of formats you no longer select, and with
`TwoDogWebPrecompress=false` it removes all of them, so a server never sends
an outdated copy.

### Compression in the page

On a server that compresses nothing, the page can download the `.gz` copies
and inflate them with the browser's built-in gzip decoder,
[`DecompressionStream`](https://developer.mozilla.org/docs/Web/API/DecompressionStream).
Chrome does not support Brotli there, so the page uses gzip. Set this in
`wwwroot/index.html`:

```javascript
const TWODOG_PCK_GZ = true;
```

The page then requests `godot.wasm.gz`, `godot.pck.gz` and the `.gz` copies
of the .NET binaries in `_framework/`. Scripts and the page itself still load
uncompressed. If a `.gz` copy is missing, the page falls back to the
original. Do not use this on a server that compresses responses, because
the files would be compressed twice.

## Web hosting with upload limits

The uncompressed bundle, engine and all, of an empty project is about 70 MiB, plus 10-15 MiB per compressed variant of the Godot engine.

If your web hosting service limits how much you can upload, ship only the gzip copies of
the engine, the pack and the .NET binaries.

That's about 18-20 MiB for an empty game.

```xml
<PropertyGroup>
  <TwoDogWebPrecompressFormats>gz</TwoDogWebPrecompressFormats>
  <TwoDogWebPrecompressKeepOriginals>false</TwoDogWebPrecompressKeepOriginals>
</PropertyGroup>
```

::: warning A minimum-size deploy is not a smaller game download
This deploy fits web hosting services that *limit how much you can upload.* It
does not make your game smaller for players. Instead, it trades their download
experience for upload size. Compared with a full deploy, players:

- download gzip, which is larger than Brotli
- cannot load the game in browsers without
  [`DecompressionStream`](https://developer.mozilla.org/docs/Web/API/DecompressionStream),
  because there are no originals to fall back to

Players get the best experience from a full deploy with all variants (Brotli,
gzip and the uncompressed originals), where the server sends each browser the
best format it accepts.
:::

The page has nothing to fall back to, so the publish turns on
[compression in the page](#compression-in-the-page) in the bundle's copy of
`index.html`. Your `wwwroot/index.html` stays unchanged. The build fails if
`gz` is not among the formats.

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
