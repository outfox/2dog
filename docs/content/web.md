# Web / Browser (WebAssembly)

2dog runs your game **in the browser** with the same inversion as desktop. Your
C# `Main()` hosts Godot and hands control to the page's render loop. The result
is a static site with no server code or special headers. Good dog. Ship it
anywhere.

2dog links the engine into the .NET WebAssembly runtime, so a C# game ships to
the web with one `dotnet publish`.

## Quickstart

Install the WebAssembly build tools and example static server once:

```bash
# One-time: the .NET wasm build tools (ships its own Emscripten)
dotnet workload install wasm-tools
dotnet tool install --global dotnet-serve
```

Then create a project or add 2dog to an existing Godot project:

::: code-group

```bash [Existing Godot Project]
cd path/to/MyGame
dnx 2dog add -y
```

```bash [Fresh Project]
dnx 2dog new MyGame -y
cd MyGame
```

:::

Publish and serve either project the same way:

```bash
dotnet publish MyGame.web
dotnet serve --directory MyGame.web/AppBundle
```

Open the served page. Output from `Main()` appears in the DevTools console.

::: tip The web host is still your code
`MyGame.web/Program.cs` is a normal 2dog host. It registers the game's plugin
initializer, starts the engine, and calls `engine.Run()`. That hands the frame
loop to the browser and returns immediately.
:::

## How it works

During `dotnet publish -r browser-wasm`, `2dog.browser-wasm`:

1. **Links Godot** (`libgodot.a`, built with Emscripten) into
   `dotnet.native.wasm`. Calls to `[LibraryImport("libgodot")]` are direct, with
   no JavaScript bridge.
2. **Exports the Godot project** as a `.pck` using the desktop editor packages
   that provide [automatic resource import](/import-tool).
3. **Builds `AppBundle/`** with the wasm, trimmed assemblies, Godot boot shell
   (`godot.js`), and pck.

At runtime, the boot shell preloads the pck, starts .NET, and runs `Main()`.

The engine uses the **Compatibility renderer** (WebGL 2). Forward+ projects
fall back through Godot's standard `rendering_method.web` setting.

## Adding web to an existing 2dog project

[`2dog add`](/add) adds the web host to an existing Godot project. The project
template includes it by default.

::: warning Host nested inside the Godot project
Keep the generated `.gdignore` and project exclusions in place. They prevent
Godot and the .NET SDK from treating web host files as game files.
:::

## Configuration

All web host properties are optional:

| Property | Default | Description |
| --- | --- | --- |
| `TwoDogWebVariant` | `release` | `debug` selects the engine build with assertions (reference `2dog.browser-wasm.debug` explicitly) |
| `TwoDogExportPack` | `true` | Export the Godot project as a `.pck` during publish; set `false` to provide `wwwroot/godot.pck` yourself |
| `TwoDogWebExportPreset` | `Web` | Export preset name in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack file name |

Add `<TrimmerRootAssembly>` for NuGet packages reached through reflection, such
as serializers or ECS libraries. The generated host already roots the game
assembly, and package targets root `GodotSharp` and `twodog`.

## The development loop

A web publish relinks the whole wasm with Emscripten and can take **minutes**.
For a shorter leash:

- **Gameplay and assets**: run the generic host
  (`dotnet run --project MyGame.2dog`) against the same engine and code.
- **Web verification**: `dotnet publish MyGame.web` from the project root
  (or `dotnet publish` inside `MyGame.web/`), then serve `AppBundle/`. The host
  defaults to Release through `Directory.Build.props`; use `-c Debug` for an
  unoptimized build.
- Browsers cache the large wasm aggressively. Hard-refresh
  (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>R</kbd>) after each publish.
- Stop your static server before republishing: the publish replaces the
  `AppBundle/` directory.

## Limitations

- **Single-threaded**: the engine uses `threads=no`, and .NET is single-threaded.
  `System.Threading` will fail. In return, no COOP/COEP headers are needed, so
  any static host works, including itch.io.
- **No external GDExtension side modules**: .NET owns the wasm main module, so
  loadable native extensions cannot be dlopened.
- **One `IL2104` trim warning** per publish is expected: GodotSharp is not
  trim-annotated upstream and is preserved whole. Your assemblies are still
  trimmed and fully analyzed.
- The web host requires the **.NET 10.0+ SDK** and `wasm-tools`. Like every
  other project, the game targets net10.0.
