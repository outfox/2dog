---
title: Web
description: "Publish your Godot C# game as a static browser site with the browser-wasm host."
---

# Web

The Web host is a `browser-wasm` host that runs Godot and your C# game in a
browser. It publishes a static site that needs no server-side code.

## Build and Serve Locally

From your Godot project directory, run these commands. Accept `add`'s
installation offer if `wasm-tools` is missing:

```bash
dnx 2dog add --web
dotnet publish MyGame.web
dnx dotnet-serve -d MyGame.web/AppBundle
```

Open the URL printed by the server. Game output appears in the browser console.
Edit `MyGame.web/wwwroot/` to customize the page, then publish again.

## Publishing

Upload the contents of `MyGame.web/AppBundle/` to a static web host.
Enable gzip or Brotli compression for faster downloads. Cross-origin isolation
headers are required only for [multithreading](#multithreading).

See [Web Configuration](/configuration/web) for build settings.

## GDExtensions

Extensions need a single-threaded WebAssembly side module listed under a `web`
key in their `.gdextension` file. With the default `TwoDogExportPack=true`,
publishing includes it in `godot.pck`. If you disable export, include the
extension in your supplied pack.
Side modules cannot use `EM_ASM` or `EM_JS`.

## Multithreading

::: warning Experimental
.NET 10 marks its multithreaded browser runtime as experimental.
:::

Set `<WasmEnableThreads>true</WasmEnableThreads>` in `MyGame.web.csproj` to
link the threaded engine. `Main`, and with it Godot's main loop, then runs on
a Web Worker, and `System.Threading` and Godot's `WorkerThreadPool` run in
parallel. Export with a preset that has `variant/thread_support=true`, set
through `TwoDogWebExportPreset`. The threaded engine has no WebXR.

The page must be cross-origin isolated, so serve it with these headers:

```bash
dnx dotnet-serve -d MyGame.web/AppBundle -h "Cross-Origin-Opener-Policy: same-origin" -h "Cross-Origin-Embedder-Policy: require-corp"
```

GDExtensions need a `-pthread` side module under a `web.wasm32.threads` key.
Every thread loads it. A thread that is busy while an extension loads picks it
up at its next wait. Threaded side modules cannot export `thread_local`
variables and are never unloaded.

## Limitations

- Godot and .NET run on one thread unless you enable
  [multithreading](#multithreading).
- Rendering uses WebGL 2 and Godot's [Compatibility renderer](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html).
- Audio, fullscreen, and XR follow browser permission and user-gesture rules.

For VR or AR, use the [WebXR host](./webxr). To embed the game in a Razor page,
use the [Blazor host](./blazor).
