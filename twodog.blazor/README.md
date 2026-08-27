# 2dog.blazor

Embed a Godot viewport in a Blazor WebAssembly page with [2dog](https://2dog.dev).

`GodotView` owns a `<canvas>` and starts the Godot engine inside the .NET runtime
Blazor already booted: 2dog links the engine (`libgodot.a`) statically into the
client's `dotnet.native.wasm`, so Godot and your Razor components share one
runtime and one thread. Razor code calls Godot objects directly - there is no
JavaScript interop layer between them.

```razor
<GodotView Project="MyGame" PluginsInitializer="TwoDogWebBoot.PluginsInitializer()"
           Started="OnStarted" style="width: 100%; height: 100vh;">
    <div class="hud">@_fps fps</div>
</GodotView>

@code {
    void OnStarted(twodog.Engine engine) => engine.Tree.Root.PrintTree();
}
```

The view needs the `2dog.engine` and `2dog.browser-wasm` packages in the Blazor
WebAssembly project; 2dog's Blazor host template wires everything up
(`dnx 2dog add --blazor`). See https://2dog.dev/hosts/blazor.
