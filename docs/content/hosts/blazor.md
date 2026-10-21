---
title: Blazor
description: "Embed Godot in a Blazor WebAssembly page with GodotView."
tagline: "Learn how to put your Godot game on a Blazor web page."
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

## Multithreading Headers

The generated server sends cross-origin isolation headers so `--web-threads`
clients can use `SharedArrayBuffer` in development and after publishing. For an
existing host, add this immediately after `builder.Build()` in
`MyGame.blazor/Program.cs`, before debugging, static assets, and endpoints:

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    context.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
    await next();
});
```

This sends `Cross-Origin-Opener-Policy: same-origin` and
`Cross-Origin-Embedder-Policy: require-corp`. Cross-origin resources must allow
loading through CORS or Cross-Origin-Resource-Policy. Use HTTPS or localhost
for the browser's secure context requirement.

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
apply to the client project. See [Blazor Configuration](/configuration/blazor)
for client build and pack settings.
