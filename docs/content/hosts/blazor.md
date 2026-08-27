---
title: Blazor Host
description: "The Blazor host embeds the Godot viewport in a Blazor Web App page: an ASP.NET Core server serves a Blazor WebAssembly client that runs Godot inside the .NET runtime Blazor already booted, so Razor code calls Godot directly."
---

# Blazor Host

`MyGame.blazor` is a [Blazor Web App](https://learn.microsoft.com/aspnet/core/blazor/hosting-models):
an ASP.NET Core server project with a Blazor WebAssembly client nested in
`Client/`. The client links the Godot engine into its `dotnet.native.wasm`,
exactly like the [browser host](./web), and shows the game through the
`GodotView` component from the `2dog.blazor` package. Godot and your Razor
components share one .NET runtime and one thread, so a page reaches the scene
tree directly - there is no JavaScript interop layer between them.

## Use It

The host is opt-in:

```bash
dnx 2dog add --blazor
dotnet new 2dog -n MyGame --blazor true
```

It needs the same .NET WebAssembly build tools as the browser host:

```bash
dotnet workload install wasm-tools
dotnet run --project MyGame.blazor
```

`dotnet run` builds the client (linking the engine and exporting the project as
`godot.pck`), then serves it from `http://localhost:5200`. Output from Godot and
your C# code lands in the browser DevTools console. `dotnet publish -c Release`
produces a deployable server in `bin/Release/net10.0/publish/`.

## Capabilities

- Razor components call Godot objects directly: same runtime, same thread.
- Ordinary HTML renders on top of the game canvas (`GodotView`'s child content).
- The canvas follows its container, the project's window size, or the browser window.
- Everything else the [browser host](./web) offers: trimmed assemblies, WebGL 2,
  content exported on the build machine.

## How It Works

Blazor boots the .NET runtime and calls the client's `Main()` as in any Blazor
WebAssembly app. `GodotView` runs after its first render: it prepares the
runtime's file system (downloading `godot.pck`), hands its canvas to Godot's
JavaScript glue, and then starts the engine in C#:

```razor
@page "/"
@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: false))

<GodotView @ref="_view" Project="MyGame" PluginsInitializer="TwoDogWebBoot.PluginsInitializer()"
           Started="OnStarted" OnFrame="OnFrame" style="width: 100%; height: 100vh;">
    <div class="hud">@_fps fps</div>
</GodotView>

@code {
    private GodotView? _view;
    private string _fps = "-";
    private long _frames;

    private void OnStarted(twodog.Engine engine) =>
        GD.Print("Scene: ", engine.Tree.CurrentScene?.Name);

    private void OnFrame()
    {
        if (++_frames % 30 != 0) return;
        _fps = Godot.Engine.GetFramesPerSecond().ToString("0");
        _ = InvokeAsync(StateHasChanged);
    }
}
```

`GodotView` registers the game's plugin initializer, creates a
[`twodog.Engine`](/api/engine) with `--main-pack godot.pck`, starts it and
calls `Run()`, which hands the frame loop to Emscripten and returns. From then
on `_view.Engine` and `_view.Tree` are live Godot objects: read or set node
properties, call methods, subscribe to signals - from event handlers or the
`OnFrame` callback.

The page must render on WebAssembly without prerendering: the engine only
exists in the browser, and a prerendered canvas would be replaced once the
client takes over.

### Lifecycle

| Member | Meaning |
| --- | --- |
| `Started` | The engine runs and its scene tree is available. |
| `OnFrame` | Called once per engine frame after the iteration; keep it cheap. |
| `Quit()` / `Exited` | Asks Godot to quit; after its asynchronous teardown the instance is destroyed and `Exited` fires. Blazor keeps running. |
| `Failed` / `Error` | Starting the engine failed; the view shows the message. |
| `Resize` | `Container` (default: the canvas follows its element), `Project` (the project's window size), `FullWindow`. |

Godot's web platform keeps engine state in module globals, so **one engine per
page load**: after `Quit()` or disposing the view, reload the page to start
again. Navigating away from the page disposes the view and quits the engine.

## Project Setup

The server project (`MyGame.blazor.csproj`) is a plain Blazor Web App server
marked for the 2dog tool:

```xml
<TwoDogBlazor>true</TwoDogBlazor>
<DefaultItemExcludes>$(DefaultItemExcludes);Client/**;TwoDogWebBoot.cs</DefaultItemExcludes>
```

The client (`Client/MyGame.blazor.Client.csproj`) is the 2dog host: a
`Microsoft.NET.Sdk.BlazorWebAssembly` project with the browser host's packages
plus `2dog.blazor`:

```xml
<PropertyGroup>
  <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
  <GodotProjectDir>../..</GodotProjectDir>
  <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="2dog.engine" Version=":2dog-version:" PrivateAssets="all"/>
  <PackageReference Include="2dog.blazor" Version=":2dog-version:"/>
  <PackageReference Include="2dog.browser-wasm" Version="[:natives-version:]" PrivateAssets="all"/>
  <ProjectReference Include="../../MyGame.csproj" PrivateAssets="all"/>
  <TrimmerRootAssembly Include="MyGame"/>
  <TrimmerRootAssembly Include="$(TargetName)"/>
</ItemGroup>
```

`PrivateAssets="all"` keeps Godot and the 2dog build targets out of the server
project, which only serves the client; `2dog.blazor` does flow to the server
(it serves the component's static assets) and has no engine dependency of its
own. `TwoDogWebBoot.cs` sits in the host folder and compiles into the game
assembly, as for the browser host.

When `2dog.browser-wasm` detects the Blazor WebAssembly SDK it leaves bundle
layout, boot and compression to Blazor: the runtime stays
`_framework/dotnet.native.wasm`, and the exported `godot.pck` plus Godot's audio
worklets become static web assets at the site root. The
[browser host's](./web#configuration) `TwoDogExportPack`,
`TwoDogWebExportPreset`, `TwoDogWebPackName`, `TwoDogWebVariant` and
`WasmInitialHeapSize` settings apply to the client project; the shell-only
options (`TwoDogWebSizeManifest`, `TwoDogWebPrecompress`, `TwoDogWebStripMaps`)
are off.

## Limitations

- Standalone Blazor WebAssembly apps work the same way (reference the three
  packages, add `GodotView` to a page); the template ships the Web App layout.
- Godot and .NET are single-threaded in this host, and Blazor renders on the
  same thread: long engine frames delay UI updates and vice versa.
- One engine instance per page load (see [Lifecycle](#lifecycle)).
- The browser host's [limitations](./web#limitations) apply: no dynamic
  GDExtensions, WebGL 2 only.
