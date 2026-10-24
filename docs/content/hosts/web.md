---
title: Godot C# Web Export
description: "Web export for Godot C#: publish your game as a static browser site, with WebGL or WebGPU, from the browser-wasm host."
tagline: "Learn how to publish your Godot C# game to the web."
keywords: "Godot web export C#, Godot HTML5 export C#, WebGPU, browser-wasm"
---

# Web

The Web host is a `browser-wasm` host that runs Godot and your C# game in a
browser. It publishes a static site that needs no server-side code.

## Build and Serve Locally

Run these commands from your Godot project directory. If `wasm-tools` is
missing, accept the offer from `add` to install it:

```bash
dnx 2dog add --web
dotnet publish MyGame.web
dnx dotnet-serve -- -d MyGame.web/AppBundle
```

Open the URL that the server prints. Game output appears in the browser
console. To customize the page, edit `MyGame.web/wwwroot/`. Then publish again.

## Publishing

Upload the contents of `MyGame.web/AppBundle/` to a static web server.
Serve it compressed for faster downloads; see
[Precompression](/configuration/web#precompression) for the settings that
match your server. Only
[multithreading](#multithreading) needs cross-origin isolation headers.

See [Web Configuration](/configuration/web) for build settings.

## GDExtensions

Extensions need a single-threaded WebAssembly side module listed under a `web`
key in their `.gdextension` file. With the default `TwoDogExportPack=true`,
`dotnet publish` adds it to `godot.pck`. If you disable the export, add the
extension to the pack that you supply.
Side modules cannot use `EM_ASM` or `EM_JS`.

## Multithreading

::: warning Experimental
.NET 10 marks its multithreaded browser runtime as experimental.
:::

Set `<WasmEnableThreads>true</WasmEnableThreads>` in `MyGame.web.csproj` to
link the threaded engine. `Main` and Godot's main loop then run on
a Web Worker. `System.Threading` and Godot's `WorkerThreadPool` run in
parallel. Use `TwoDogWebExportPreset` to select an export preset that has
`variant/thread_support=true`. The threaded engine has no WebXR.

The page must be cross-origin isolated, so serve it with these headers:

```bash
dnx dotnet-serve -- -d MyGame.web/AppBundle -h "Cross-Origin-Opener-Policy: same-origin" -h "Cross-Origin-Embedder-Policy: require-corp"
```

GDExtensions need a `-pthread` side module under a `web.wasm32.threads` key.
Threaded side modules cannot export `thread_local` variables and are never
unloaded.

## Limitations

- Godot and .NET run on one thread unless you enable
  [multithreading](#multithreading).
- Rendering uses WebGL 2 and Godot's [Compatibility renderer](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html).
- Audio, fullscreen, and XR follow browser permission and user-gesture rules.

For VR or AR, use the [WebXR host](./webxr). To embed the game in a Razor page,
use the [Blazor host](./blazor).
