# 2dog.browser-wasm

Browser (WebAssembly) support for [2dog](https://2dog.dev): statically links the
Godot engine into your .NET `browser-wasm` publish and ships the Godot web boot
shell. Your C# code hosts Godot in the browser  –  the same inversion 2dog does on
desktop.

## How it works

The .NET browser-wasm runtime is the Emscripten main module. During
`dotnet publish -r browser-wasm`, the wasm-tools workload relinks
`dotnet.native.wasm` with Godot's static archive (`libgodot.a`) and JS glue from
this package. Game content is exported to a `.pck` on your build machine (via
the desktop editor packages 2dog already depends on) and served next to the app
bundle.

## Requirements

- .NET SDK 10.0+ with the `wasm-tools` workload (`dotnet workload install wasm-tools`)
- A host project: `net10.0`, `OutputType=Exe`, `RuntimeIdentifier=browser-wasm`,
  `PackageReference` to `2dog` and `2dog.browser-wasm`, `<GodotProjectDir>` set
- The Godot project needs a `Web` export preset, a solution file, and
  `LIBGODOT_ENABLED` + `AllowUnsafeBlocks` in its csproj
- A `wwwroot/index.html` boot page (create a project with
  `dotnet new 2dog --web` for a working setup out of the box)

## Usage

```
dotnet publish
```

(The 2dog web host template defaults the configuration to Release; pass
`-c Debug` for an unoptimized build.)

Serve the generated `AppBundle/` directory with any static file server. The
single-threaded engine build requires no COOP/COEP headers.

## Properties

| Property | Default | Description |
| --- | --- | --- |
| `TwoDogWebVariant` | `release` | `debug` selects the engine-assertions build (requires referencing `2dog.browser-wasm.debug`) |
| `TwoDogExportPack` | `true` | Export the Godot project as a `.pck` during publish |
| `TwoDogWebExportPreset` | `Web` | Export preset name in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack file name |

## Known limitations

- Single-threaded (`System.Threading` use will fail); a threaded variant may come later
- No external GDExtension side modules (the .NET runtime owns the main module)
- Every publish relinks the wasm (minutes); iterate gameplay on desktop, publish web to verify
