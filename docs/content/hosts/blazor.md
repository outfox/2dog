---
title: Blazor
description: "Embed Godot in a Blazor WebAssembly page with GodotView."
---

# Blazor

The Blazor host embeds Godot in a Blazor WebAssembly page through `GodotView`.
An ASP.NET Core server serves the client; Razor components can access Godot
objects directly.

## Use It

From your Godot project directory, run these commands. Accept `add`'s
installation offer if `wasm-tools` is missing:

```bash
dnx 2dog add --blazor
dotnet run --project MyGame.blazor
```

Open the URL printed by the server. To publish the server and client:

```bash
dotnet publish MyGame.blazor -c Release
```

## Customize the Page

Edit `MyGame.blazor/Client/Pages/Home.razor`. Keep WebAssembly rendering and
disable prerendering:

```razor
@page "/"
@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))

<GodotView Project="MyGame" PluginsInitializer="TwoDogWebBoot.PluginsInitializer()"
           style="width: 100%; height: 100vh;" />
```

Use `Started` to access the engine, `OnFrame` for per-frame work, and child
content to overlay HTML on the game. One engine can run at a time.

Godot and Blazor share one thread. The [Web host's limitations](./web#limitations)
and [pack settings](/configuration#web-host) apply to the client project.
