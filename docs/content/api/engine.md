---
title: twodog.Engine
description: "API reference for twodog.Engine, which configures, starts, and owns one embedded Godot instance: constructor, properties, Start, Run, Dispose, and common Godot arguments."
tagline: "Learn how to configure, start and run Godot from your own .NET code."
---

# `twodog.Engine`

Configures, starts, and owns one embedded Godot instance.

```csharp
public class Engine : IDisposable, IAsyncDisposable
```

**Package:** `2dog.engine`  
**Namespace:** `twodog`

## Constructor

```csharp
public Engine(string project, string? path = null, params string[] args)
```

| Parameter | Description                                                                    |
|-----------|--------------------------------------------------------------------------------|
| `project` | Label passed as Godot's first argument; this is not a content path             |
| `path`    | Directory containing `project.godot`; omit it for automatic content resolution |
| `args`    | Additional Godot command-line arguments, passed unchanged                      |

When `path` is omitted or `null`, desktop hosts call
[`ResolveContent()`](#resolvecontent). Browser hosts leave the path unset so
Godot loads the web pack. Pass a path only for a nonstandard content location.
2dog resolves relative paths from the working directory of the process.

## Properties

### `CaptureErrors` and `Errors`

```csharp
public bool CaptureErrors { get; init; }
public GodotErrorLog Errors { get; }
```

Set `CaptureErrors = true` before `Start()` to collect Godot's native errors and
warnings in `Errors` (`twodog.GodotErrorLog`). It defaults to `false`.
Capture does not work in a browser. If you enable it there, `Start()` throws
`PlatformNotSupportedException`. The [testing fixtures](./fixture-base#errors)
enable capture automatically and expose the same log.

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

### `Completion`

```csharp
public Task Completion { get; }
```

Completes when this `Engine` reaches a terminal state. If `Start()` succeeded,
that means the owned native instance has been destroyed and synchronous `Exited`
handlers have returned. `Engine` does not await asynchronous work that those handlers start.
Shutdown restores the host synchronization context before `Exited` fires. As a
result, later awaits in the host do not depend on Godot's stopped frame loop.

### `Exited`

```csharp
public event Action? Exited;
```

Fires once after a successfully started instance is destroyed. `Completion`
also completes for an `Engine` disposed before it starts, but `Exited` does not fire.

## Threading

Make lifecycle calls and access Godot objects only on the thread that called
`Start()`. Calls from another thread fail, so dispatch them to the host's engine
thread.

## Methods

### `Start`

```csharp
public GodotInstance Start()
```

Starts Godot and the project's `run/main_scene`, then returns a borrowed
`GodotInstance` compatibility handle. `Engine` owns the instance; do not dispose
the handle. If you start another instance in the same assembly load context before
completion, `Start()` throws `InvalidOperationException`.

### `Iteration`

```csharp
public bool Iteration()
```

Processes one main-loop frame. Returns `true` when Godot requests exit.
Dispose the engine after the main loop stops. `Iteration()` throws
`InvalidOperationException` if you call it recursively or while `Run()` owns
the main loop.

### `RequestQuit`

```csharp
public void RequestQuit()
```

Requests a graceful quit. Repeated requests are harmless.

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

Stops and destroys the owned instance synchronously on desktop. In a browser,
`Dispose()` requests shutdown and returns before teardown; use `DisposeAsync()`
or await `Completion`.

### `DisposeAsync`

```csharp
public ValueTask DisposeAsync()
```

Requests shutdown and waits for teardown. Prefer this in browser hosts.
If you dispose from a game callback, 2dog defers the disposal until the native
frame returns. Do not wait synchronously for `Completion` inside that callback.

### `WebExitRuntimeOnQuit`

```csharp
public static bool WebExitRuntimeOnQuit { get; set; }
```

Defaults to `false` in a browser: quitting destroys Godot and keeps .NET alive
so any host can start another engine. Standalone pages may set it to `true`
to terminate the entire WebAssembly runtime on quit. Blazor keeps it `false`.
Browser restarts require a fresh canvas and host configuration before `Start()`;
`GodotView` handles those browser resources automatically.

### `WebLogFirstChanceExceptions`

```csharp
public static bool WebLogFirstChanceExceptions { get; set; }
```

Defaults to `false`. Set it to `true` before browser startup to print every thrown
managed exception to the browser console as an error. This includes caught
exceptions and exceptions stored in unawaited tasks. Set it back to `false` when
you are done, because this diagnostic mode adds logging overhead. On desktop,
the setter throws `PlatformNotSupportedException`.

In a browser, 2dog logs unhandled managed exceptions and unobserved task
exceptions automatically, from the first use of `Engine` and across restarts.
2dog reports unobserved task exceptions only after a garbage collection.

### `ResolveContent`

```csharp
public static string? ResolveContent()
```

Returns `null` when a `.pck` named after the executable is next to it, so that
Godot loads that pack. Otherwise, returns [`ResolveProjectDir()`](#resolveprojectdir).
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
`TwoDogWebBoot.PluginsInitializer()`, which `2dog.godotsharp` generates into
the game assembly. Desktop calls throw `PlatformNotSupportedException`.

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
        engine.Start();

        while (!engine.Iteration())
        {
            // One frame has completed.
        }
    }
}
```

`args` accepts ordinary
[Godot command-line arguments](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html),
such as `--headless`, `--verbose`, `--rendering-driver opengl3` and
`--audio-driver Dummy`. It passes them to the engine unchanged.

See [2dog (generic .NET)](../hosts/generic) to run and publish a desktop host.
