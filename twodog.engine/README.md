# 2dog.engine

Embed the Godot engine in your .NET applications.

2dog lets a standard .NET application host Godot and use the GodotSharp API.

## Quick Start

Scaffold a host beside an existing `project.godot`:

```bash
dnx 2dog add
```

```csharp
using twodog;

using var engine = new Engine("MyGodotApp", args: args);
engine.Start();

while (!engine.Iteration())
{
    // Your code runs here every frame.
}
```

`Engine` owns the instance; `Start()` returns a borrowed `GodotInstance`
compatibility handle. Use `RequestQuit()`, `Completion`, and `Exited` for
lifecycle control. Desktop disposal is synchronous; browser code awaits
`DisposeAsync()`. After completion, create a new `Engine` to restart.
Pump and access Godot from the thread that called `Start()`; do not iterate and
dispose the same engine concurrently from different threads.

The generated host embeds its `GodotProjectDir`. The constructor uses that
source directory during development and the adjacent `.pck` after publish.

To create a new project instead:

```bash
dnx 2dog new MyGodotApp
```

## What's Included

- **twodog.dll** - engine hosting API
- **GodotSharp.dll** - Godot C# bindings
- **Godot.SourceGenerators** and **GodotPlugins** - script generation and loading
- **Automatic asset import** - incremental import during build

Platform-specific native libraries are provided by transitive dependencies (`2dog.win-x64`, `2dog.linux-x64`, `2dog.osx-arm64`); the GodotTools assemblies used by the automatic import come from `2dog.tools`.

## Documentation

- [Getting Started](https://github.com/outfox/2dog)
- [API Reference](https://github.com/outfox/2dog)
