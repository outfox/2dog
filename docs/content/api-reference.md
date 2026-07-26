# API Reference

## `twodog.Engine`

`Engine` configures, starts, and owns an embedded Godot instance. It implements
`IDisposable`.

### Constructor

```csharp
public Engine(string project, string? path = null, params string[] args)
```

| Parameter | Description |
| --- | --- |
| `project` | Project name passed as the first Godot argument |
| `path` | Optional Godot project directory, passed through `--path` |
| `args` | Additional Godot command-line arguments |

### Properties

| Member | Type | Description |
| --- | --- | --- |
| `Tree` | `SceneTree` | Active scene tree; throws if Godot has not started |
| `NativePath` | `string?` (`init`) | Exact desktop libgodot path to load instead of variant probing; not supported in the browser |
| `ProjectAssemblyDir` | `string?` (`init`) | Preferred directory for the project's C# assembly; defaults to `AppContext.BaseDirectory` |
| `LoadedNativePath` | `static string?` | Full path of the loaded libgodot, when known |

`NativePath` and `ProjectAssemblyDir` are advanced hosting controls. Most
applications should leave them unset.

### Methods

#### `Start`

```csharp
public GodotInstance Start()
```

Starts Godot, including the project's `run/main_scene`, and returns the object
that controls its main loop. Starting another instance before disposing the
current engine throws `InvalidOperationException`; sequential restart is
supported. See [Single Godot Instance](/known-issues/single-instance).

#### `Run`

```csharp
public void Run(Action? perFrame = null)
```

Requires a successful `Start()`. On desktop, calls `perFrame` after each
iteration that does not request exit, then blocks until Godot requests exit.
In the browser, it registers the Emscripten main loop and returns immediately.

#### `Dispose`

```csharp
public void Dispose()
```

Destroys the instance owned by this engine. A `using` statement is the
recommended leash.

#### `ResolveProjectDir`

```csharp
public static string ResolveProjectDir()
```

Returns the absolute `GodotProjectDir` embedded in loaded assembly metadata.
Throws `InvalidOperationException` when no loaded assembly contains it.

#### `RegisterWebPluginsInitializer`

```csharp
public static void RegisterWebPluginsInitializer(IntPtr initializer)
```

Registers the game assembly's source-generated plugins initializer. Browser
hosts must call this before `Start()`; desktop calls throw
`PlatformNotSupportedException`. The generated `TwoDogWebBoot.cs` exposes the
required pointer. See [Browser Hosts](/hosts/web).

### Example

```csharp
using System;
using Engine = twodog.Engine;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var engine = new Engine(
            "myapp",
            Engine.ResolveProjectDir(),
            "--headless",
            "--audio-driver",
            "Dummy");

        using var godot = engine.Start();
        engine.Run();
    }
}
```

The example uses one `engine` and one `godot` declaration, so it can be pasted
into a console project without duplicate-local errors.

## `Godot.GodotInstance`

`Engine.Start()` returns the running Godot instance.

### `Iteration`

```csharp
public bool Iteration()
```

Processes one main-loop frame. Returns `true` when Godot wants to quit.

```csharp
while (!godot.Iteration())
{
    // One frame has completed.
}
```

### `Dispose`

Shuts down the instance. Dispose it before its owning `Engine`.

## Common Godot Arguments

| Argument | Purpose |
| --- | --- |
| `--headless` | Run without display or audio output |
| `--verbose` | Enable verbose logging |
| `--debug` | Enable debug mode |
| `--rendering-driver <driver>` | Select a rendering driver, such as `opengl3` |
| `--audio-driver <driver>` | Select an audio driver, such as `Dummy` |

Arguments are passed to Godot verbatim through the constructor.
