---
title: twodog.Engine
description: "API reference for twodog.Engine, which configures, starts, and owns one embedded Godot instance: constructor, properties, Start, Run, Dispose, and common Godot arguments."
---

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
| `project` | Label passed as Godot's first argument; this is not a content path |
| `path` | Directory containing `project.godot`; omit it for automatic content resolution |
| `args` | Additional Godot command-line arguments, passed unchanged |

When `path` is omitted or `null`, desktop hosts call
[`ResolveContent()`](#resolvecontent). Browser hosts leave the path unset so
Godot loads the web pack. Pass a path only for a nonstandard content location.
Relative paths are resolved from the process working directory.

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

### `ResolveContent`

```csharp
public static string? ResolveContent()
```

Returns `null` when a `.pck` named after the executable sits beside it, letting
Godot load that pack. Otherwise, returns [`ResolveProjectDir()`](#resolveprojectdir).
The desktop constructor calls this automatically when `path` is omitted.

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
The web host's `Program.cs` calls this before `Start()` with the pointer from
`TwoDogWebBoot.PluginsInitializer()` (the bootstrap file in the web host
folder, compiled into the game assembly); normal application code does not
need anything beyond that template line. Desktop calls throw
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
        using var engine = new Engine("MyGame", args: args);
        using var godot = engine.Start();

        while (!godot.Iteration())
        {
            // One frame has completed.
        }
    }
}
```

`args` accepts ordinary
[Godot command-line arguments](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html) – 
`--headless`, `--verbose`, `--rendering-driver opengl3`, `--audio-driver Dummy`,
and the rest – and passes them to the engine unchanged.

See [Generic Host](../hosts/generic) for the full desktop-host pattern.
