---
title: Single Godot Instance
description: "Only one Godot instance may run per assembly load context in 2dog; sequential restart is supported and isolated concurrent hosting is experimental."
---

# Single Godot Instance Per Load Context

Only one Godot instance may run in an assembly load context at a time. Starting
a second instance throws `InvalidOperationException`:

```csharp
using var engine1 = new Engine("MyGame");
engine1.Start();

using var engine2 = new Engine("MyGame");
engine2.Start(); // Throws InvalidOperationException.
```

Sequential restart is supported in packages based on Godot 4.7 and later,
including the browser: the [Blazor host](/hosts/blazor) restarts on a fresh
canvas, and web hosts keep the runtime alive by default
(`Engine.WebExitRuntimeOnQuit = false`). Any host can start a new engine after
`Completion` completes and `Exited` fires. `Engine` owns its instance; the
`GodotInstance` returned by `Start()` is a borrowed compatibility handle.

On desktop, dispose the current engine before creating another:

```csharp
using (var engine = new Engine("MyGame"))
{
    engine.Start();
    engine.Run();
} // Synchronous teardown.

using var nextEngine = new Engine("MyGame");
nextEngine.Start();
```

In a browser, call `RequestQuit()` while `Run()` pumps frames and await
`Completion`, or call `DisposeAsync()`
before creating the new `Engine`; browser teardown is asynchronous.

This allows xUnit collections to use fresh engines sequentially in one test
process. Collections that share an engine must disable parallelization; see
[Testing](../testing).

A restart does not reload the game assembly: static fields keep their values
across engines, while every Godot object the previous engine created was
disposed when it shut down. A static that holds a Godot object - a `Resource`
such as `FastNoiseLite`, a `Node`, a `PackedScene` - therefore throws
`ObjectDisposedException` in the next engine. Create such objects per instance
(in `_Ready`, or lazily behind `GodotObject.IsInstanceValid`), or reset them
from `Exited`:

```csharp
private static FastNoiseLite? _noise;
private static FastNoiseLite Noise =>
    GodotObject.IsInstanceValid(_noise) ? _noise : _noise = new FastNoiseLite();
```
