---
title: Blazor
description: "Embed Godot in a Blazor WebAssembly page with GodotView."
tagline: "Learn how to put your Godot game on a Blazor web page."
---

# Blazor

The Blazor host embeds Godot in a Blazor WebAssembly page through `GodotView`.
An ASP.NET Core server serves the client. Razor components can access Godot
objects directly.

## Use It

Run these commands from your Godot project directory. If `wasm-tools` is
missing, accept the offer from `add` to install it:

```bash
dnx 2dog add --blazor
dotnet run --project MyGame.blazor
```

Open the URL that the server prints. To publish the server and the client,
run this command:

```bash
dotnet publish MyGame.blazor -c Release
```

## Multithreading Headers

The generated server sends cross-origin isolation headers so `--web-threads`
clients can use `SharedArrayBuffer` in development and after publishing. For an
existing host, add this code to `MyGame.blazor/Program.cs`. Put it immediately
after `builder.Build()` and before the calls that add debugging, static assets,
and endpoints:

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    context.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
    await next();
});
```

This sends `Cross-Origin-Opener-Policy: same-origin` and
`Cross-Origin-Embedder-Policy: require-corp`. Resources from other origins must allow
the load through CORS or a Cross-Origin-Resource-Policy header. Use HTTPS or
localhost, because the browser requires a secure context.

## Customize the Page

Edit `MyGame.blazor/Client/Pages/Home.razor`. Keep WebAssembly rendering and
disable prerendering:

```razor
@page "/"
@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))

<GodotView Project="MyGame" PluginsInitializer="TwoDogWebBoot.PluginsInitializer()"
           style="width: 100%; height: 100vh;" />
```

- Use `Started` to access the engine.
- Use `OnFrame` for work in each frame.
- Use child content to show HTML over the game.

Only one engine can run at a time.

Godot and Blazor share one thread. The [Web host's limitations](./web#limitations)
apply to the client project. See [Blazor Configuration](/configuration/blazor)
for client build and pack settings.
