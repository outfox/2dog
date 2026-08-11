---
title: Browser Host
description: "The browser-wasm 2dog host: the HTML that embeds your game, program structure, host properties, and publishing the AppBundle directory as a static site."
---

# Browser Host (WASM / HTML5 / WebXR)

`MyGame.web` is a `browser-wasm` host plus the HTML that embeds your game (you can edit it in the sub-folder `wwwroot`). Its
published `MyGame.web/AppBundle/` directory is a static site.

```bash
dotnet publish MyGame.web
```

If you need multiple HTML variants, you can run `dnx 2dog add` multiple to and add and name more browser hosts, one for each deployment.

::: info This page covers host anatomy
[Web / Browser (WASM)](/web) is the guide to publishing, serving, development,
and browser limits.
:::

## Program

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

This differs from the [generic host](./generic) in three ways:

1. Register `TwoDogWebBoot.PluginsInitializer()` before `Start()`. The browser
   cannot load `GodotPlugins.dll` from disk, so the game assembly exposes its
   source-generated initializer directly. `TwoDogWebBoot.cs` lives in this host
   folder but compiles into the *game* assembly through a guarded
   `Compile Include` in the game csproj - scripts are resolved from the
   assembly holding the initializer.
2. Omit the project path. In the browser the constructor leaves it unset, and
   the page mounts the exported `godot.pck` through `--main-pack`.
3. Do not dispose the engine or call `Iteration()`. `Run()` hands the loop to
   Emscripten and returns immediately. Use `Run(perFrame)` for host-side frame
   work.

Skipping initializer registration fails during `Start()`. Calling the browser
registration API on desktop throws `PlatformNotSupportedException`.

## Project Differences

The shared host project is documented in [Hosts](./). The browser host adds:

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
</ItemGroup>

<ItemGroup>
  <TrimmerRootAssembly Include="MyGame"/>
  <TrimmerRootAssembly Include="$(TargetName)"/>
</ItemGroup>
```

`2dog.browser-wasm` links `libgodot.a`, exports the `.pck`, and assembles
`AppBundle/`. The game and host assemblies are rooted because Godot resolves
scripts through reflection and publishing trims unused code.

The host also contains:

- `.gdignore`, which keeps Godot out of the host folder;
- `Directory.Build.props`, which defaults browser builds to `Release`;
- `global.json`, which pins a .NET 10 SDK;
- `wwwroot/`, which contains the page shell and static files;
- `TwoDogWebBoot.cs`, the web bootstrap compiled by the game project (see
  [Program](#program)); this host excludes it from its own compile globs.

## Host Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogWebVariant` | `release` | Use `debug` with an explicit `2dog.browser-wasm.debug` reference |
| `TwoDogExportPack` | `true` | Export the project during publish; `false` uses your `wwwroot/godot.pck` |
| `TwoDogWebExportPreset` | `Web` | Export preset in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack name |
| `TwoDogWebSizeManifest` | `true` | Write `twodog.sizes.json` for the shell's progress bar |
| `TwoDogWebStripMaps` | `true` for release | Delete `*.js.map` sourcemaps from the bundle |
| `TwoDogWebPrecompress` | `true` | Write `.br`/`.gz` siblings next to payload files |

Loading-performance knobs (`WasmInitialHeapSize`, `WasmEmitSymbolMap`,
compression and serving guidance) are covered in
[Web / Browser (WASM)](/web#loading-performance).

Publishing requires a .NET 10 SDK with the wasm workload:

```bash
dotnet workload install wasm-tools
```

## WebXR

Godot's [WebXR interface](https://docs.godotengine.org/en/stable/tutorials/xr/setting_up_webxr.html)
works from this host too - drop the
[WebXR Layers polyfill](https://github.com/immersive-web/webxr-layers-polyfill) into `wwwroot/`,
then load *and instantiate* it before `godot.js`
(`<script>if (navigator.xr) new WebXRLayersPolyfill();</script>` - loading the script alone
polyfills nothing). The [WebXR host](./webxr) ships that polyfill prewired and documents the
game project's XR opt-in, the C# session pattern, and testing without a headset.
