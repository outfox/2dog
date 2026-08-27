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
  `dnx 2dog new MyGame --web` for a working setup out of the box)
- Or a Blazor WebAssembly project: the package detects the Blazor SDK and
  leaves layout and boot to Blazor; `2dog.blazor`'s `GodotView` starts the
  engine (`dnx 2dog new MyGame --blazor`)

## Usage

```
dotnet publish
```

(The 2dog web host template defaults the configuration to Release; pass
`-c Debug` for an unoptimized build.)

Serve the generated `AppBundle/` directory with any static file server. The
single-threaded engine build requires no COOP/COEP headers.

## Configuration and limitations

See the [Browser Host documentation](https://2dog.dev/hosts/web) for the full
property reference, loading-performance guidance, deployment behavior, and
current browser limitations.
