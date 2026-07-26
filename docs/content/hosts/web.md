# Browser Host

`MyGame.web` is a `browser-wasm` host plus the HTML that embeds your game. Its
published `MyGame.web/AppBundle/` directory is a static site.

```bash
dotnet publish MyGame.web
```

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

        var engine = new Engine("MyGame", null, args);
        engine.Start();
        GD.Print("2dog is running in the browser!");
        engine.Run();
        return 0;
    }
}
```

This differs from the [console host](./console) in three ways:

1. Register `TwoDogWebBoot.PluginsInitializer()` before `Start()`. The browser
   cannot load `GodotPlugins.dll` from disk, so the game assembly exposes its
   source-generated initializer directly.
2. Pass `null` as the project path. At runtime, the page mounts the exported
   `godot.pck` through `--main-pack`; `GodotProjectDir` is still needed to
   export that pack at build time.
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
- `wwwroot/`, which contains the page shell and static files.

## Host Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogWebVariant` | `release` | Use `debug` with an explicit `2dog.browser-wasm.debug` reference |
| `TwoDogExportPack` | `true` | Export the project during publish; `false` uses your `wwwroot/godot.pck` |
| `TwoDogWebExportPreset` | `Web` | Export preset in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack name |

Publishing requires a .NET 10 SDK with the wasm workload:

```bash
dotnet workload install wasm-tools
```

Continue with [Web / Browser (WASM)](/web) for the publishing workflow.
