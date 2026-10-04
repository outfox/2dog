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
headers are not required.

See [Web Configuration](/configuration/web) for build settings.

## GDExtensions

Extensions need a single-threaded WebAssembly side module listed under a `web`
key in their `.gdextension` file. With the default `TwoDogExportPack=true`,
publishing includes it in `godot.pck`. If you disable export, include the
extension in your supplied pack.
Side modules cannot use `EM_ASM` or `EM_JS`.

## Limitations

- Godot and .NET run on one thread; `System.Threading` is unsupported.
- Rendering uses WebGL 2 and Godot's [Compatibility renderer](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html).
- Audio, fullscreen, and XR follow browser permission and user-gesture rules.

For VR or AR, use the [WebXR host](./webxr). To embed the game in a Razor page,
use the [Blazor host](./blazor).
