# 2dog

Embed the Godot engine in your .NET applications.

2dog inverts the traditional Godot architecture: instead of Godot driving your application, **you** control Godot as an embedded library. This gives you full access to the GodotSharp API from a standard .NET project with familiar tooling.

## Quick Start

This package includes a `dotnet new` project template. Install the package and scaffold a new project:

```bash
dotnet new install 2dog
dotnet new 2dog -n MyGodotApp
cd MyGodotApp
```

Or add 2dog to an existing project:

```bash
dotnet add package 2dog
```

```csharp
using twodog;

using var engine = new Engine("MyGodotApp", "./project");
using var godot = engine.Start();

while (!godot.Iteration())
{
    // Your code runs here every frame
}
```

## What's Included

- **twodog.dll** - Engine API for embedding Godot
- **GodotSharp.dll** - Full Godot C# API bindings
- **Godot.SourceGenerators** - Roslyn source generators for Godot node types
- **GodotPlugins** - Runtime plugin loader
- **`dotnet new` template** - Scaffold new 2dog projects with `dotnet new 2dog`

Platform-specific native libraries are provided by transitive dependencies (`2dog.win-x64`, `2dog.linux-x64`, `2dog.osx-arm64`).

## Documentation

- [Getting Started](https://github.com/outfox/2dog)
- [API Reference](https://github.com/outfox/2dog)
