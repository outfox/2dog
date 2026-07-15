# 2dog.engine

Embed the Godot engine in your .NET applications.

2dog inverts the traditional Godot architecture: instead of Godot driving your application, **you** control Godot as an embedded library. This gives you full access to the GodotSharp API from a standard .NET project with familiar tooling.

## Quick Start

Add the engine to your project:

```bash
dotnet add package 2dog.engine
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

To scaffold a complete new project (or convert an existing Godot project), use the [`2dog`](https://www.nuget.org/packages/2dog) tool/template package instead:

```bash
dotnet new install 2dog
dotnet new 2dog -n MyGodotApp
```

## What's Included

- **twodog.dll** - Engine API for embedding Godot
- **GodotSharp.dll** - Full Godot C# API bindings
- **Godot.SourceGenerators** - Roslyn source generators for Godot node types
- **GodotPlugins** - Runtime plugin loader
- **Automatic asset import** - an incremental MSBuild step imports your Godot project (`.uid` files, textures, script UID cache) during build; no Godot editor installation needed

Platform-specific native libraries are provided by transitive dependencies (`2dog.win-x64`, `2dog.linux-x64`, `2dog.osx-arm64`); the GodotTools assemblies used by the automatic import come from `2dog.tools`.

## Documentation

- [Getting Started](https://github.com/outfox/2dog)
- [API Reference](https://github.com/outfox/2dog)
