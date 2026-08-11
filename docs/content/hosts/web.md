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
is compiled into the web natives and registered on `XRServer` automatically - no host changes are
needed. An XR game opts in with three pieces:

1. **Project settings**: enable XR shaders in `project.godot`:

   ```ini
   [xr]
   shaders/enabled.web=true
   ```

   (The `.web` feature-tag override avoids compiling the extra shader variants on desktop hosts.)
   Add an `XROrigin3D` with an `XRCamera3D` to your scene.

2. **A user gesture**: browsers only start XR sessions from a user gesture, and a Godot UI
   button press qualifies - no HTML button is required:

   ```csharp
   var webxr = (WebXRInterface)XRServer.FindInterface("WebXR");
   webxr.SessionSupported += (mode, supported) => _enterVrButton.Visible = supported;
   webxr.SessionStarted += () => GetViewport().UseXR = true;
   webxr.IsSessionSupported("immersive-vr");

   // In the button's Pressed handler:
   webxr.SessionMode = "immersive-vr";
   webxr.RequestedReferenceSpaceTypes = "bounded-floor, local-floor, local";
   webxr.RequiredFeatures = "local-floor";
   webxr.Initialize();
   ```

   On desktop hosts `FindInterface("WebXR")` returns `null` - guard and skip.

3. **The WebXR Layers polyfill**: Godot renders through the
   [WebXR Layers API](https://www.w3.org/TR/webxrlayers-1/), which desktop Chrome and the
   [Immersive Web Emulator](https://chromewebstore.google.com/detail/immersive-web-emulator/cgffilbpcibhmcfbgggfhfolhkfbhmik)
   do not implement natively (the Meta Quest browser does). Drop
   [webxr-layers-polyfill](https://github.com/immersive-web/webxr-layers-polyfill) into `wwwroot/`
   (use 1.1.0 or newer - 1.0.3 allocates its layer textures with invalid WebGL formats)
   and load it before `godot.js` in your page:

   ```html
   <script src="webxr-layers-polyfill.min.js"></script>
   <script>new WebXRLayersPolyfill();</script>
   ```

WebXR needs a secure context: `localhost` works for development; anything else requires HTTPS. To
test on a headset against a dev machine, either serve over HTTPS or use
`adb reverse tcp:8080 tcp:8080` on a Quest so the game stays on `localhost`. The showcase's
`main.tscn` + `WebXR.cs` demonstrate the full pattern.
