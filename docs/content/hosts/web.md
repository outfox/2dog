# Browser Host

`MyGame.web` is a `browser-wasm` project plus the HTML that embeds and runs
your game in the browser. It is a .NET WebAssembly application: your `Main()`
starts, hosts the Godot engine, and hands the frame loop to the page. The
published output in `MyGame.web/AppBundle/` is a plain static site.

```bash
dotnet publish MyGame.web
```

::: info This page is the host project
[Web / Browser (WASM)](/web) covers publishing, serving, the development loop,
and the renderer and threading limits. What follows is the host itself.
:::

## The Program

```csharp
using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    private static int Main(string[] args)
    {
        // The Godot project's assembly owns the source-generated plugins
        // initializer; register it before Start() (there is no
        // GodotPlugins.dll on web).
        Engine.RegisterWebPluginsInitializer(TwoDogWebBoot.PluginsInitializer());

        // args come from the page's GODOT_CONFIG.args plus the
        // '--main-pack godot.pck' the engine loader prepends.
        var engine = new Engine("MyGame", null, args);
        engine.Start();

        GD.Print("2dog is running in the browser!");

        // Hands the loop to emscripten and returns immediately; the engine
        // destroys itself when Godot requests quit. Do not dispose here.
        engine.Run();

        return 0;
    }
}
```

Three differences from the console host, and they are the only ones:

**1. The plugins initializer is registered by hand.** On desktop, Godot loads
`GodotPlugins.dll` from disk to bind your C# scripts; the browser has no
filesystem to load it from. `TwoDogWebBoot.cs`  –  which lives in the **Godot
project**, not the host  –  exposes the source-generated initializer, and the
host registers a pointer to it before `Start()`. Both directions fail loudly:
skipping it on web throws from `Start()`, calling it on desktop throws
`PlatformNotSupportedException`.

**2. The project path is `null`.** There is no project directory at runtime;
the content is baked into `godot.pck`, preloaded by the page and mounted via
`--main-pack`. `GodotProjectDir` still matters at *build* time  –  it is what
the publish exports the pack from.

**3. `Run()` returns immediately, and you must not dispose.** Emscripten owns
the main loop, so `Main()` finishes while the game keeps running and the
engine destroys itself on quit. No `using`, no `Dispose()`, no
`while (!Iteration())`. Host-side frame logic goes in the optional
`Run(perFrame)` callback.

## The Project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <RootNamespace>MyGame.Web</RootNamespace>
  </PropertyGroup>

  <PropertyGroup>
    <GodotProjectDir>..</GodotProjectDir>
    <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog.engine" Version=":2dog-version:"/>
    <PackageReference Include="2dog.browser-wasm" Version="[:natives-version:]"/>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../MyGame.csproj"/>
  </ItemGroup>

  <!-- Scripts are resolved by reflection: root the game assembly -->
  <ItemGroup>
    <TrimmerRootAssembly Include="MyGame"/>
    <TrimmerRootAssembly Include="$(TargetName)"/>
  </ItemGroup>
</Project>
```

Compared with the console host:

| Difference | Why |
| --- | --- |
| `RuntimeIdentifier=browser-wasm` | A wasm application, not a desktop executable |
| `2dog.browser-wasm` | Statically links `libgodot.a` into `dotnet.native.wasm`, exports the `.pck`, assembles `AppBundle/` |
| `TrimmerRootAssembly` | Publishing trims; scripts are found by reflection, so the game assembly must be rooted |
| No `TwoDogVariant` | The web engine variant comes from `TwoDogWebVariant` instead |
| No `app.manifest` / `[STAThread]` | Windows desktop concerns |

Beside the csproj sit four files: a `.gdignore` (keeps the Godot exporter
out), a `Directory.Build.props` defaulting this host to `Release` because
browser bundles are only useful optimized, a `global.json` pinning a .NET 10
SDK, and `wwwroot/` holding the page shell and static files.

::: warning Publishing needs the wasm workload
`dotnet workload install wasm-tools`, once. The SDK pins exist because the
publish only works on a .NET 10 SDK that has it.
:::

### Host Properties

| Property | Default | Description |
| --- | --- | --- |
| `TwoDogWebVariant` | `release` | `debug` selects the engine build with assertions (reference `2dog.browser-wasm.debug` explicitly) |
| `TwoDogExportPack` | `true` | Export the Godot project as a `.pck` during publish; `false` to supply `wwwroot/godot.pck` yourself |
| `TwoDogWebExportPreset` | `Web` | Export preset name in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack file name |

## Building and Serving

```bash
# From the project root (the root global.json pins the SDK)
dotnet publish MyGame.web

# Any static file server will do
dotnet serve --directory MyGame.web/AppBundle
```

A publish relinks the whole wasm with Emscripten, so expect minutes rather
than seconds. Iterate against the [console host](./console)  –  same engine,
same code  –  and publish to verify.
