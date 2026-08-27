---
title: Browser Host
description: "The 2dog browser host packages a Godot C# game as a static WebAssembly site; learn its runtime model, configuration, performance controls, and limitations."
---

# Browser Host

`MyGame.web` runs Godot and your C# game in a browser. Its editable `wwwroot`
directory provides the page shell, and `AppBundle` is the resulting static site.
It needs no server-side code or cross-origin isolation headers.

## Use It

New projects include this host by default. Use `dnx 2dog add` to add one to an
existing project. You can add and name multiple browser hosts when deployments
need different HTML shells.

## Build and Serve Locally

Install the .NET WebAssembly build tools and a static file server once:

```bash
dotnet workload install wasm-tools
dotnet tool install --global dotnet-serve
```

Publish the host, then serve its `AppBundle` directory:

```bash
dotnet publish MyGame.web
dotnet serve --directory MyGame.web/AppBundle -z -b
```

Open the URL printed by `dotnet serve`; output from the host's `Main()` lands
in the browser DevTools console. `-z -b` compresses responses with gzip and
Brotli  –  the engine, .NET runtime, and pack are large files, and serving them
uncompressed makes local startup unnecessarily slow.

Publish again after changing game resources, host code, or files in `wwwroot`.
Restart `dotnet serve` only when you change its options.

## Capabilities

- Runs Godot C# through the .NET WebAssembly runtime and WebGL 2.
- Publishes a self-contained static site for any ordinary static host.
- Trims managed assemblies and precompresses large output files.
- Supports Godot's WebXR interface; the [WebXR host](./webxr) adds the Layers
  polyfill needed by browsers without native support.
- The same engine link powers the [Blazor host](./blazor), where a Razor
  component owns the canvas and calls Godot directly.

## How It Works

During publish, `2dog.browser-wasm` links `libgodot.a` into the .NET WebAssembly
main module, exports the Godot project as `godot.pck`, and assembles both with
the .NET runtime and page shell in `AppBundle`.

At runtime, the shell downloads the engine and pack in parallel, starts .NET,
and calls the host's `Main()`:

```csharp
using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    private static int Main(string[] args)
    {
        Engine.RegisterWebPluginsInitializer(TwoDogWebBoot.PluginsInitializer());

        var engine = new Engine("MyGame", args: args);
        engine.Start();
        GD.Print("2dog is running in the browser!");
        engine.Run();
        return 0;
    }
}
```

The browser host differs from the [generic host](./generic) in three ways:

1. It registers the source-generated plugin initializer before `Start()`
   because the browser cannot load `GodotPlugins.dll` from disk.
2. It leaves the project path unset because the page mounts `godot.pck` through
   `--main-pack`.
3. It calls `Run()` instead of `Iteration()`. `Run()` hands the frame loop to
   Emscripten and returns immediately; `Run(perFrame)` adds host-side frame work.

## Project Setup

The browser host adds the `browser-wasm` runtime identifier and native package
to the [shared host project](./):

```xml
<PropertyGroup>
  <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
  <GodotProjectDir>..</GodotProjectDir>
  <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="2dog.engine" Version=":2dog-version:"/>
  <PackageReference Include="2dog.browser-wasm" Version="[:natives-version:]"/>
  <ProjectReference Include="../MyGame.csproj"/>
  <TrimmerRootAssembly Include="MyGame"/>
  <TrimmerRootAssembly Include="$(TargetName)"/>
</ItemGroup>
```

The host also contains:

- `wwwroot/` for the page shell and static files;
- `TwoDogWebBoot.cs`, compiled into the game assembly for plugin registration;
- `Directory.Build.props`, which defaults browser builds to `Release`;
- `global.json`, which pins the required .NET 10 SDK;
- `.gdignore`, which keeps Godot out of the host directory.

Add `<TrimmerRootAssembly>` for libraries reached only through reflection. The
generated host already roots the game, host, `GodotSharp`, and `twodog`
assemblies.

## Configuration

All properties are optional:

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogWebVariant` | `release` | Select the engine build; `debug` requires an explicit `2dog.browser-wasm.debug` reference |
| `TwoDogExportPack` | `true` | Export the project; `false` uses `wwwroot/godot.pck` instead |
| `TwoDogWebExportPreset` | `Web` | Select the preset in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Set the deployed pack name |
| `TwoDogWebSizeManifest` | `true` | Write `twodog.sizes.json` for determinate loading progress |
| `TwoDogWebStripMaps` | `true` for release | Remove `*.js.map` files from the bundle |
| `TwoDogWebPrecompress` | `true` | Write `.br` and `.gz` siblings for sizeable files |
| `TwoDogWebPrecompressLevel` | `Optimal` | Set sibling compression; `SmallestSize` trades publish time for size |
| `WasmEmitSymbolMap` | `false` | Include native symbols for stack traces at about 20 MB per load |
| `WasmInitialHeapSize` | `256MB` | Set initial linear memory; memory growth remains enabled |

Raise `WasmInitialHeapSize` for content-heavy games or lower it toward `128MB`
for low-end mobile devices after testing.

## Loading Performance

Startup size comes mainly from the engine and trimmed managed code in
`godot.wasm`, the .NET runtime in `_framework/`, and your content in
`godot.pck`. The shell downloads the engine and pack in parallel and reads
`twodog.sizes.json` to show progress.

### Compression

Compression is the largest download-time improvement. By default, publish
writes `.br` and `.gz` siblings. Disable them with
`-p:TwoDogWebPrecompress=false` when an upload limit or deployment pipeline
makes the doubled on-disk bundle undesirable.

- `dotnet serve -z -b` compresses local responses on the fly.
- Most static hosts and CDNs negotiate gzip or Brotli automatically.
- itch.io compresses uploaded wasm and pack files, so precompressed siblings
  are unnecessary.
- nginx, Caddy, and similar servers can serve the generated siblings directly.
- If a host cannot compress, set `TWODOG_PCK_GZ` to `true` in
  `wwwroot/index.html` and preload `godot.pck.gz`; the shell inflates it with
  `DecompressionStream`.

Verify a deployment with:

```bash
curl -sI -H 'Accept-Encoding: br, gzip' https://your.host/godot.wasm
```

Look for a `Content-Encoding` response header.

### Diagnosing Startup

The shell logs time spent downloading and starting, a size table for large
files, and `2dog:boot`, `2dog:downloads-done`, and `2dog:first-frame`
performance marks.

- Slow downloads mean the output needs compression or a smaller pack.
- Slow startup after download comes from wasm compilation, .NET startup, and
  first-scene content.
- Use DevTools network throttling with its cache disabled for realistic tests.

List pack contents from largest to smallest with:

```bash
2dog pack list MyGame.web/AppBundle/godot.pck
```

Large packs commonly contain PCM audio, oversized lossless textures, or files
included by a broad export filter. Prefer Ogg Vorbis for long audio, review
texture imports, and exclude non-game directories or mark them with `.gdignore`.

## WebXR

Godot's [WebXR interface](https://docs.godotengine.org/en/stable/tutorials/xr/setting_up_webxr.html)
is included. Browsers without the WebXR Layers API need the
[WebXR Layers polyfill](https://github.com/immersive-web/webxr-layers-polyfill)
loaded and instantiated before `godot.js`. The [WebXR host](./webxr) provides
that setup.

## Limitations

- Godot and .NET are single-threaded in this host. `System.Threading` is not
  supported.
- The Compatibility renderer uses WebGL 2. Forward+ projects fall back through
  Godot's `rendering_method.web` setting.
- Native GDExtension side modules cannot be loaded because .NET owns the wasm
  main module.
- Browser platform policies still apply, including user gestures for audio,
  fullscreen, and XR.
