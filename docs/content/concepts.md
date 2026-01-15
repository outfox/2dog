# Core Concepts

## Inverted Architecture

Traditional Godot applications have Godot control the process lifecycle:

```
Godot Process → SceneTree → Your Scripts
```

**2dog inverts this:**

```
Your .NET Process → twodog.Engine → Godot (as library)
```

You control when Godot starts, when frames iterate, and when it shuts down. Godot becomes a rendering/physics/audio library that you drive.

## libgodot Embedding

2dog uses `libgodot`, a shared library build of Godot Engine. This allows:

- Godot to run as an embedded library within your .NET process
- Direct P/Invoke calls to Godot's native APIs
- Full access to GodotSharp managed bindings

The native library (`libgodot.dll`, `libgodot.so`, or `libgodot.dylib`) is automatically downloaded and cached on first build.

### Build Variants

2dog supports three Godot build variants, each optimized for different use cases:

#### Template Builds (Runtime-Only)

**Template Debug** (`template_debug`):
- Debug symbols enabled for troubleshooting
- Assertions and error checking enabled
- No editor features
- Suitable for: Development, debugging game logic

**Template Release** (`template_release`):
- Fully optimized for performance
- Minimal binary size
- No debug symbols or editor features
- Suitable for: Production games and applications

#### Editor Build (Development Tools)

**Editor** (`editor`):
- Full editor toolchain enabled (`TOOLS_ENABLED`)
- Resource import pipeline available
- Editor APIs: `EditorInterface`, `EditorPlugin`, `ImportPlugin`
- Larger binary size, slower than templates
- Suitable for: Asset import tools, editor extensions, build pipelines

::: tip Choosing a Build Variant
- **Building a game?** Use Debug during development, Release for production
- **Need to import assets?** Use Editor configuration
- **Creating build tools?** Use Editor configuration
- **Running in CI/CD?** Use Debug for tests, Release for final builds
:::

Build with different variants:
```bash
dotnet build -c Debug    # template_debug
dotnet build -c Release  # template_release
dotnet build -c Editor   # editor with TOOLS_ENABLED
```

## GodotSharp API Access

Once the engine starts, you have full access to the GodotSharp API:

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

Unlike traditional Godot where `_Process` callbacks drive your code, you explicitly pump the main loop:

```csharp
while (!godot.Iteration())
{
    // Called every frame
    // Godot processes physics, rendering, input, etc.
    
    // Your frame logic here
    if (someCondition)
        break; // Exit when you decide
}
```

`Iteration()` returns `true` when Godot wants to quit (e.g., window closed).

## Single Instance Limitation

::: warning
Only one Godot instance can exist per process. This is a Godot limitation, not a 2dog limitation.
:::

```csharp
// This works
using var engine = new Engine("app", "./project");
using var godot = engine.Start();

// This throws InvalidOperationException
using var engine2 = new Engine("app2", "./project2");
var godot2 = engine2.Start(); // ❌ Error!
```

For testing scenarios, use xUnit's collection fixtures to share a single instance. See [Testing](./testing).

## Resource Paths

Godot's `res://` paths resolve relative to the project directory you specify:

```csharp
// Project at ./my_project
new Engine("app", "./my_project");

// res://scenes/main.tscn → ./my_project/scenes/main.tscn
var scene = GD.Load<PackedScene>("res://scenes/main.tscn");
```
