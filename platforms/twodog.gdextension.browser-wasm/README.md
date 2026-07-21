# 2dog.gdextension.browser-wasm

Browser (WebAssembly) support for the [2dog](https://2dog.dev) GDExtension
stack: statically links the non-mono Godot engine into your .NET
`browser-wasm` publish and ships the Godot web boot shell. Your C# code hosts
Godot in the browser – the same inversion 2dog does on desktop, without the
mono module.

## How it works

The .NET browser-wasm runtime is the Emscripten main module. During
`dotnet publish -r browser-wasm`, the wasm-tools workload relinks
`dotnet.native.wasm` with Godot's static archive (`libgodot.a`) and JS glue
from this package. The 2dog.gdextension host registers itself through the
engine's embedded-extension init callback – no GodotPlugins, no side modules,
no dynamic linking.

## Requirements

- .NET SDK 10.0+ with the `wasm-tools` workload (`dotnet workload install wasm-tools`)
- A host project: `net10.0`, `OutputType=Exe`, `RuntimeIdentifier=browser-wasm`,
  `PackageReference` to `2dog.gdextension` and `2dog.gdextension.browser-wasm`
- A `wwwroot/index.html` boot page

## Usage

```
dotnet publish
```

Serve the generated `AppBundle/` directory with any static file server. The
single-threaded engine build requires no COOP/COEP headers.

## Properties

| Property | Default | Description |
| --- | --- | --- |
| `TwoDogWebVariant` | `release` | `debug` selects the engine-assertions build (requires referencing `2dog.gdextension.browser-wasm.debug`) |

## Known limitations

- Single-threaded (`System.Threading` use will fail); a threaded variant may come later
- No external GDExtension side modules (the .NET runtime owns the main module); the host itself is the embedded extension
- Single engine lifetime per page (no restart)
- Every publish relinks the wasm (minutes); iterate gameplay on desktop, publish web to verify
