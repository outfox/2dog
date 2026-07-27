# `twodog.Engine`

Configures, starts, and owns one embedded Godot instance.

```csharp
public class Engine : IDisposable
```

**Package:** `2dog.engine`  
**Namespace:** `twodog`

## Constructor

```csharp
public Engine(string project, string? path = null, params string[] args)
```

| Parameter | Description |
| --- | --- |
| `project` | Project name passed as Godot's first argument |
| `path` | Directory containing `project.godot`; adds `--path` when set |
| `args` | Additional Godot command-line arguments, passed unchanged |

Use [`ResolveProjectDir()`](#resolveprojectdir) for `path` in a standard 2dog
host. The value comes from the host project's `<GodotProjectDir>` setting.

## Properties

### `Tree`

```csharp
public SceneTree Tree { get; }
```

Returns the active scene tree. Access it only after `Start()` succeeds.

### `NativePath`

```csharp
public string? NativePath { get; init; }
```

Loads an exact desktop `libgodot` file instead of selecting a packaged variant.
Leave this unset in normal hosts. It is not supported in the browser.

### `ProjectAssemblyDir`

```csharp
public string? ProjectAssemblyDir { get; init; }
```

Sets the preferred directory for the game's C# assembly. The default is
`AppContext.BaseDirectory`; custom isolated hosts may need a different path.

### `LoadedNativePath`

```csharp
public static string? LoadedNativePath { get; }
```

Returns the full path of the loaded desktop `libgodot`, when known.

## Methods

### `Start`

```csharp
public GodotInstance Start()
```

Starts Godot and the project's `run/main_scene`, then returns its running
instance. Starting a second classic instance before disposing the first throws
`InvalidOperationException`.

### `Run`

```csharp
public void Run(Action? perFrame = null)
```

Runs the main loop after `Start()`.

- On desktop, it blocks until Godot requests exit. `perFrame` runs after each
  completed frame that did not request exit.
- In the browser, it registers the Emscripten main loop and returns immediately.

### `Dispose`

```csharp
public void Dispose()
```

Stops and destroys the instance owned by this engine. Dispose the
[`GodotInstance`](./godot-instance) first.

### `ResolveProjectDir`

```csharp
public static string ResolveProjectDir()
```

Returns the absolute `GodotProjectDir` embedded in loaded assembly metadata.
It throws `InvalidOperationException` when no loaded assembly provides that
metadata.

### `RegisterWebPluginsInitializer`

```csharp
public static void RegisterWebPluginsInitializer(IntPtr initializer)
```

Registers the game assembly's generated plugin initializer in a browser host.
The generated `TwoDogWebBoot.cs` calls this before `Start()`; normal application
code does not need to call it. Desktop calls throw
`PlatformNotSupportedException`.

## Example

```csharp
using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var engine = new Engine("MyGame", Engine.ResolveProjectDir(), args);
        using var godot = engine.Start();

        while (!godot.Iteration())
        {
            // One frame has completed.
        }
    }
}
```

## Common Godot Arguments

| Argument | Purpose |
| --- | --- |
| `--headless` | Run without a window |
| `--verbose` | Enable verbose logging |
| `--debug` | Enable debug mode |
| `--rendering-driver <driver>` | Select a rendering driver, such as `opengl3` |
| `--audio-driver <driver>` | Select an audio driver, such as `Dummy` |

See [Generic Host](../hosts/generic) for the full desktop-host pattern.
