# API Reference

## twodog.Engine

The main entry point for embedding Godot in your application.

### Constructor

```csharp
public Engine(string project, string? path = null, params string[] args)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `project` | `string` | Project name (used in logging and identification) |
| `path` | `string?` | Path to the Godot project directory. Maps to `--path` argument |
| `args` | `string[]` | Additional command-line arguments passed to Godot |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Tree` | `SceneTree` | The active scene tree. Throws if called before `Start()` |

### Methods

#### Start()

```csharp
public GodotInstance Start()
```

Initializes and starts the Godot engine. Returns a `GodotInstance` for controlling the main loop.

::: danger
Can only be called once per process. Subsequent calls throw `InvalidOperationException`.
:::

### Example

```csharp
using twodog;

// Basic usage
using var engine = new Engine("myapp", "./project");
using var godot = engine.Start();

// With additional arguments
using var engine = new Engine("myapp", "./project", "--headless", "--verbose");
using var godot = engine.Start();
```

## Godot.GodotInstance

Represents a running Godot instance. Returned by `Engine.Start()`.

### Methods

#### Iteration()

```csharp
public bool Iteration()
```

Processes one frame of the Godot main loop (physics, rendering, input, etc.).

**Returns:** `true` if Godot wants to quit, `false` to continue.

```csharp
while (!godot.Iteration())
{
    // Frame processed
}
// Godot requested quit
```

#### Dispose()

Shuts down the Godot instance. Called automatically when using `using` statements.

## Common Godot Arguments

Pass these via the `args` parameter:

| Argument | Description |
|----------|-------------|
| `--headless` | Run without rendering (for servers/CI) |
| `--verbose` | Enable verbose logging |
| `--debug` | Enable debug mode |
| `--rendering-driver` | Set renderer: `vulkan`, `opengl3`, `dummy` |
| `--audio-driver` | Set audio: `PulseAudio`, `WASAPI`, `Dummy` |

```csharp
// Headless server
new Engine("server", "./project", "--headless", "--audio-driver", "Dummy");

// Force OpenGL
new Engine("app", "./project", "--rendering-driver", "opengl3");
```
