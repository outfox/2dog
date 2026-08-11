---
title: Web / Browser (WebAssembly)
description: "Ship your Godot C# game to the browser with one dotnet publish: 2dog links the engine into the .NET WebAssembly runtime and outputs a static site with no server code."
---

# Web / Browser (WebAssembly)

2dog exports your game **for the browser** using the same principle as running it as a desktop .NET app.

Your C# `Main()` (in `MyGame.web/Program.cs`) hosts Godot and hands control to the page's render loop. The result
is a static site with no server code or special headers. 

*Ship. Good dog!*

2dog links the engine into the .NET WebAssembly runtime and creates the necessary host project. Your Godot C# game gets bundled for the web simpy via `dotnet publish`.

## Quickstart

Install the WebAssembly build tools and example static server once:

```bash
# One-time: the .NET wasm build tools (ships its own Emscripten)
dotnet workload install wasm-tools
dotnet tool install --global dotnet-serve
```

Then create a project or add 2dog to an existing Godot project:

::: code-group

```bash [Existing Godot Project]
cd path/to/MyGame
dnx 2dog add
```

```bash [Fresh Project]
dnx 2dog new MyGame
cd MyGame
```

:::

Publish and serve either project the same way:

```bash
dotnet publish MyGame.web
dotnet serve --directory MyGame.web/AppBundle -z -b
```

(`-z -b` enables gzip/brotli response compression - see
[Loading Performance](#loading-performance).)

Open the served page. Output from `Main()` appears in the DevTools console.

::: tip The web host is still your code
`MyGame.web/Program.cs` is almost an entirely normal host. It registers the game's plugin
initializer, starts the engine, and calls `engine.Run()`, which hands the frame loop to the 
browser and returns immediately.
:::

## How it works

During `dotnet publish -r browser-wasm`, `2dog.browser-wasm`:

1. **Links Godot** (`libgodot.a`, built with Emscripten) into
   `dotnet.native.wasm`. Calls to `[LibraryImport("libgodot")]` are direct, with
   no JavaScript bridge.
2. **Exports the Godot project** as a `.pck` using the desktop editor packages
   that provide [automatic resource import](/import-tool).
3. **Builds `AppBundle/`** with the wasm, trimmed assemblies, Godot boot shell
   (`godot.js`), and pck.

At runtime, the boot shell preloads the pck, starts .NET, and runs `Main()`.

The engine uses the **Compatibility renderer** (WebGL 2). Forward+ projects
fall back through Godot's standard `rendering_method.web` setting.

## Adding web to an existing 2dog project

[`2dog add`](/add) adds the web host to an existing Godot project. The project
template includes it by default.

::: warning Host nested inside the Godot project
Keep the generated `.gdignore` and project exclusions in place. They prevent
Godot and the .NET SDK from treating web host files as game files.
:::

## Configuration

All web host properties are optional:

| Property | Default | Description |
| --- | --- | --- |
| `TwoDogWebVariant` | `release` | `debug` selects the engine build with assertions (reference `2dog.browser-wasm.debug` explicitly) |
| `TwoDogExportPack` | `true` | Export the Godot project as a `.pck` during publish; set `false` to provide `wwwroot/godot.pck` yourself |
| `TwoDogWebExportPreset` | `Web` | Export preset name in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack file name |
| `TwoDogWebSizeManifest` | `true` | Write `twodog.sizes.json` (exact wasm/pck byte sizes); the boot shell reads it to render a determinate progress bar |
| `TwoDogWebStripMaps` | `true` for release | Delete `*.js.map` sourcemaps from the bundle |
| `TwoDogWebPrecompress` | `true` | Write `.br`/`.gz` siblings next to every sizeable bundle file |
| `TwoDogWebPrecompressLevel` | `Optimal` | `CompressionLevel` for the siblings; `SmallestSize` compresses harder at higher publish cost |
| `WasmEmitSymbolMap` | `false` | `true` restores `dotnet.native.js.symbols` (~20 MB, downloaded at every boot) for symbolicated native stack traces |
| `WasmInitialHeapSize` | `256MB` | Initial linear memory; growth stays enabled. Raise (e.g. `512MB`) for pck-heavy desktop-targeted games, lower toward `128MB` for low-end mobile |

Add `<TrimmerRootAssembly>` for NuGet packages reached through reflection, such
as serializers or ECS libraries. The generated host already roots the game
assembly, and package targets root `GodotSharp` and `twodog`.

## Loading Performance

A publish is sized by three things: `godot.wasm` (the engine plus your
trimmed .NET code, tens of MB), the `_framework/` runtime files (a few MB),
and `godot.pck` (your content - see [Shrinking the pck](#shrinking-the-pck)).
The shell downloads the wasm and pck in parallel and shows the 2dog splash
with a progress bar fed by `twodog.sizes.json`.

### Serve compressed

Compression is the single biggest lever for download time. Each publish
writes `.br` and `.gz` siblings next to the payload files
(`TwoDogWebPrecompress`; `TwoDogWebPrecompressLevel` defaults to `Optimal`,
use `SmallestSize` for final deploys). The siblings roughly double the bundle
on disk - opt out with `-p:TwoDogWebPrecompress=false` where that matters
(upload-size-limited hosts, CI artifact uploads, deploy pipelines that ship
the whole `AppBundle`).

- **Dev**: `dotnet serve --directory MyGame.web/AppBundle -z -b` compresses
  responses on the fly (no siblings needed).
- **Static hosts / CDNs** (GitHub Pages, statichost.eu, Cloudflare, ...):
  most negotiate gzip or brotli transparently. Probe yours:
  `curl -sI -H 'Accept-Encoding: br, gzip' https://your.host/godot.wasm | grep -i content-encoding`
- **itch.io**: upload uncompressed; its CDN gzips wasm and pck itself.
  Publish with `-p:TwoDogWebPrecompress=false` for smaller uploads.
- **Self-hosted**: nginx (`gzip_static on; brotli_static on;`), Caddy
  (`file_server { precompressed br gzip }`), and static-web-server serve the
  siblings as-is with `Content-Encoding`.
- **Hosts that compress nothing**: the shell has an opt-in fallback that
  fetches the `.gz` siblings and inflates them in the page
  (`DecompressionStream`). Flip `TWODOG_PCK_GZ` to `true` in
  `wwwroot/index.html` (and swap the `godot.pck` preload hint for
  `godot.pck.gz`).

### Diagnosing slow startup

The shell logs `2dog: first frame N ms after boot (X ms downloading, Y ms
starting)` plus a size/time table of every large download, and emits
`performance.mark`s (`2dog:boot`, `2dog:downloads-done`, `2dog:first-frame`)
visible in the DevTools Performance panel.

- A long *downloading* phase is bandwidth: serve compressed (above) and shrink
  the pck (below). Test realistically with DevTools network throttling and
  "Disable cache".
- A long *starting* phase is wasm compilation, .NET startup, and your first
  scene load - largely proportional to wasm size and first-scene content.
- Localhost is disk-speed; a hang there is almost never the network.

### Shrinking the pck

List what the pack actually contains, largest first:

```bash
dotnet <path-to>/2dog.import.dll --list-pack MyGame.web/AppBundle/godot.pck
```

Usual offenders:

- **Audio**: WAV imports default to uncompressed PCM. Import music and long
  effects as Ogg Vorbis, or set the WAV import's compress mode.
- **Textures**: enormous lossless sources; check per-texture import overrides
  and consider lossy VRAM compression for large ones.
- **Export filter**: `export_filter="all_resources"` plus stray files in the
  project tree. Use the preset's exclude filter, and `.gdignore` folders that
  are not game content.

`dotnet/include_debug_symbols` in the Web preset does not matter here:
pack-only web exports carry no assemblies (the host publish supplies them).
The template preset sets it to `false` so the file does not imply otherwise.

## Limitations

- **Single-threaded**: the engine uses `threads=no`, and .NET is single-threaded.
  `System.Threading` will fail. In return, no COOP/COEP headers are needed, so
  any static host works, including [itch.io](https://itch.io).
- **No external GDExtension side modules**: .NET owns the wasm main module, so
  loadable native extensions cannot be dlopened.
