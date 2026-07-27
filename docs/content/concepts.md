# Core Concepts

Teaching an old robot new tricks!

## Inverted Architecture

Traditional Godot applications have Godot control the process lifecycle:

```
Godot Process → SceneTree → Your Scripts
```

2dog inverts this:

```
Your .NET Process → twodog.Engine → Godot (as library)
```

Your .NET process controls startup, frames, and shutdown. Godot becomes a
rendering, physics, and audio library that your application drives.

## libgodot Embedding

2dog uses `libgodot`, a shared-library build of Godot Engine. It runs inside
your .NET process, supports direct P/Invoke calls to native APIs, and retains
full access to GodotSharp managed bindings.

The native library (`libgodot.dll`, `libgodot.so`, or `libgodot.dylib`) ships in
the `2dog.win-x64`, `2dog.linux-x64`, or `2dog.osx-arm64` NuGet package.
`2dog.engine` references the appropriate packages automatically.

### Build Variants

Generated hosts map .NET configurations to Godot variants with
`TwoDogVariant`. Other projects must configure this mapping explicitly.

#### Template Builds (Runtime-Only)

**Template Debug** (`template_debug`) includes assertions and additional error
checking, but no editor features. Use it for development.

**Template Release** (`template_release`) is optimized for production, without
debug symbols or editor features.

#### Editor Build (Development Tools)

**Editor** (`editor`) enables `TOOLS_ENABLED`, the resource-import pipeline, and
editor APIs such as `EditorInterface`, `EditorPlugin`, and `ImportPlugin`. It is
larger and slower than template builds.

::: tip Choosing a Build Variant
[Build Configurations](./build-configurations) is the complete guide to choosing
and configuring variants. Resource import selects the editor variant
automatically; see [Resource Import](./import-tool).
:::

```bash
# Scaffolded projects use these mappings:
dotnet build -c Debug    # template_debug
dotnet build -c Release  # template_release
dotnet build -c Editor   # editor with TOOLS_ENABLED
```

## GodotSharp API Access

After startup, the full GodotSharp API is available:

```csharp
using var engine = new Engine("app", "./project");
using var godot = engine.Start();

// Access the scene tree
SceneTree tree = engine.Tree;

// Load and instantiate scenes
var scene = GD.Load<PackedScene>("res://my_scene.tscn");
var instance = scene.Instantiate();
tree.Root.AddChild(instance);

// Use any Godot API
var viewport = tree.Root.GetViewport();
var physics = PhysicsServer3D.Singleton;
```

## The Main Loop

Unlike traditional Godot, the host explicitly pumps the main loop:

```csharp
while (!godot.Iteration())
{
    // Godot processes physics, rendering, input, and your frame logic here.
    if (someCondition)
        break; // Exit when you decide
}
```

`Iteration()` returns `true` when Godot wants to quit, such as when the window closes.

## Single Instance Limitation

Only one Godot instance can run per assembly load context at a time. Sequential
restart is supported. See [Single Godot Instance](./known-issues/single-instance)
for examples and the experimental isolated-hosting path.

## Resource Paths

Godot resolves `res://` paths relative to the project directory passed to `Engine`:

```csharp
// Project at ./my_project
new Engine("app", "./my_project");

// res://scenes/main.tscn → ./my_project/scenes/main.tscn
var scene = GD.Load<PackedScene>("res://scenes/main.tscn");
```
