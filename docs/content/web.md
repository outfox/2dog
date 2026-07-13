# Web / Browser (WebAssembly)

2dog runs your game **in the browser**  –  with the same inversion it gives you on
desktop. Your C# `Main()` starts, hosts the Godot engine, and hands control to
the page's render loop. The publish output is a plain static site: no server
code, no special headers, host it anywhere.

This is something the stock Godot + C# combination cannot do today: 2dog links
the engine *into* the .NET WebAssembly runtime, so C# games ship to the web
with a single `dotnet publish`.

## Quickstart

```bash
# One-time: the .NET wasm build tools (ships its own Emscripten)
dotnet workload install wasm-tools

# Create a project - the web host is included by default
dotnet new 2dog -n LetsCook

# Publish the browser app (imports assets and exports the game
# content automatically)
cd LetsCook/LetsCook.Web
dotnet publish -c Release

# Serve the result - any static file server works
dotnet serve --directory AppBundle
```

Open the served page: your game is running in the browser, and everything your
`Main()` prints lands in the DevTools console.

::: tip The web host is still your code
`LetsCook.Web/Program.cs` is a normal 2dog host  –  it registers the game's
plugin initializer, starts the engine, and calls `engine.Run()`, which hands
the frame loop to the browser and returns immediately.
:::

## How it works

During `dotnet publish -r browser-wasm`, the `2dog.browser-wasm` package:

1. **Statically links the Godot engine** (`libgodot.a`, built with Emscripten)
   into the .NET runtime's `dotnet.native.wasm`  –  every
   `[LibraryImport("libgodot")]` becomes a direct call, no JS bridge.
2. **Exports your Godot project as a `.pck`** on the build machine, using the
   same desktop editor packages that power 2dog's automatic asset import.
3. **Assembles the app bundle**: the wasm, your trimmed assemblies, the Godot
   boot shell (`godot.js`), and the pck, ready to serve from `AppBundle/`.

At runtime the page's boot shell preloads the pck, boots the .NET runtime, and
runs your `Main()`  –  .NET is the host, in the browser just like on desktop.

The engine renders with the **Compatibility renderer** (WebGL 2). Projects
configured for Forward+ fall back automatically via Godot's standard
`rendering_method.web` setting.

## Adding web to an existing 2dog project

The template does this for you; for an existing setup you need:

1. **In the Godot project:**
   - `<DefineConstants>$(DefineConstants);LIBGODOT_ENABLED</DefineConstants>`
     and `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in the csproj (exposes
     the source-generated plugins initializer),
   - a `TwoDogWebBoot.cs` exposing a pointer to the source-generated plugins
     initializer (copy it from the template  –  it must live in the Godot
     project's assembly, where scripts are looked up),
   - an `export_presets.cfg` with a `Web` preset,
   - a solution file next to `project.godot` (GodotTools requires one during
     export).
2. **A web host project** (net10.0+, `RuntimeIdentifier=browser-wasm`)
   referencing the `2dog` and `2dog.browser-wasm` packages, with
   `<GodotProjectDir>` pointing at the Godot project  –  again, easiest copied
   from a `dotnet new 2dog` output.

::: warning Godot project at the repository root?
If `project.godot` lives at your repo root (common for jam projects), two
extras: put a `.gdignore` file in the web host directory so the Godot importer
skips it, and add `<Compile Remove="YourGame.Web/**"/>` to the Godot project's
csproj so the .NET SDK's source glob doesn't swallow the host's sources.
:::

## Configuration

Properties for the web host project (all optional):

| Property | Default | Description |
| --- | --- | --- |
| `TwoDogWebVariant` | `release` | `debug` selects the engine build with assertions (reference `2dog.browser-wasm.debug` explicitly) |
| `TwoDogExportPack` | `true` | Export the Godot project as a `.pck` during publish; set `false` to provide `wwwroot/godot.pck` yourself |
| `TwoDogWebExportPreset` | `Web` | Export preset name in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack file name |

Add a `<TrimmerRootAssembly>` for any NuGet package your game reaches via
reflection (serializers, ECS libraries, ...). The game assembly, `GodotSharp`,
and `twodog` are rooted for you.

## The development loop

A web publish relinks the entire wasm with Emscripten  –  expect **minutes, every
publish**. That cost is inherent to static linking, so iterate the fast way:

- **Gameplay and assets**: run the desktop host (`dotnet run`)  –  it's the same
  engine and the same code.
- **Web verification**: `dotnet publish -c Release` in the web host, then
  serve `AppBundle/`.
- Browsers cache the large wasm aggressively  –  hard-refresh
  (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>R</kbd>) after each publish.
- Stop your static server before republishing: the publish replaces the
  `AppBundle/` directory.

## Limitations

- **Single-threaded**: the engine is built `threads=no` and the .NET runtime
  runs single-threaded  –  `System.Threading` use will fail. (No COOP/COEP
  headers needed in return, so the output runs on any static host, including
  itch.io.)
- **No GDExtension** in web builds  –  the .NET runtime owns the wasm main
  module, so native side modules cannot be loaded.
- **One `IL2104` trim warning** per publish is expected: GodotSharp is not
  trim-annotated upstream and is deliberately preserved whole. Your own
  assemblies are still trimmed and fully analyzed.
- The .NET SDK for the web host must be **10.0+** with the `wasm-tools`
  workload; the game project itself stays on net8.0.
