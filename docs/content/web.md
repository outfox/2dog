---
title: Web / Browser (WebAssembly)
description: "Build a 2dog browser host and serve its WebAssembly output locally with dotnet serve."
---

# Web / Browser (WebAssembly)

The browser host publishes your Godot C# game as a static WebAssembly site. This
page covers building that site and running it locally. See [Browser Host](/hosts/web)
for how the host works, its configuration, and browser limitations.

## Prerequisites

Install the .NET WebAssembly build tools and `dotnet-serve` once:

```bash
dotnet workload install wasm-tools
dotnet tool install --global dotnet-serve
```

## Create a Browser Host

New 2dog projects include a browser host by default:

```bash
dnx 2dog new MyGame
cd MyGame
```

To add one to an existing Godot or 2dog project:

```bash
cd path/to/MyGame
dnx 2dog add
```

The generated host is `MyGame.web`. Keep its `.gdignore` and project exclusions
in place so Godot and the .NET SDK do not treat host files as game files.

## Build and Serve

Publish the host, then serve its `AppBundle` directory:

```bash
dotnet publish MyGame.web
dotnet serve --directory MyGame.web/AppBundle -z -b
```

Open the URL printed by `dotnet serve`. Output from the host's `Main()` appears
in the browser DevTools console.

`-z -b` enables gzip and Brotli compression. This matters because the engine,
.NET runtime, and game pack are large files; serving the directory without
compression makes local startup unnecessarily slow.

Run `dotnet publish` again after changing game resources, host code, or files in
`wwwroot`. Stop and restart `dotnet serve` only if you change its options.

The output in `MyGame.web/AppBundle/` is a static site. Deployment choices,
compression behavior, package sizing, and all MSBuild properties are documented
on the [Browser Host page](/hosts/web).
