---
title: Single Godot Instance
description: "Only one Godot instance may run per assembly load context in 2dog; sequential restart is supported and isolated concurrent hosting is experimental."
tagline: "Learn why one Godot runs per load context, and how to restart it."
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

Sequential restart is supported, also in the browser. The
[Blazor host](/hosts/blazor) restarts on a fresh canvas, and web hosts keep the
runtime alive by default (`Engine.WebExitRuntimeOnQuit = false`). Any host can start a new engine after
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

In a browser, teardown is asynchronous. Before you create the new `Engine`, call
`RequestQuit()` while `Run()` pumps frames, then await `Completion`. You can
also call `DisposeAsync()` instead.

This allows xUnit collections to use fresh engines sequentially in one test
process with the same display mode. Keep headless and rendering fixtures in
separate test projects. A mode change across native restarts can crash Godot,
so the fixtures reject it. Collections must disable parallelization. See
[Testing](/testing/).

Before shutdown returns, it releases every Godot object that the engine
created. This includes wrappers that the garbage collector dropped but did not
finalize yet. As a result, nothing from one engine is released into the next. You do not need to force a
garbage collection between engines.

A restart does not reload the game assembly. Static fields keep their values
across engines, but the previous engine disposed every Godot object that it
created when it shut down. As a result, a static that holds a Godot object
throws `ObjectDisposedException` in the next engine. Examples are a `Resource`
such as `FastNoiseLite`, a `Node` or a `PackedScene`. Create such objects per instance
(in `_Ready`, or lazily behind `GodotObject.IsInstanceValid`), or reset them
from `Exited`:

```csharp
private static FastNoiseLite? _noise;
private static FastNoiseLite Noise =>
    GodotObject.IsInstanceValid(_noise) ? _noise : _noise = new FastNoiseLite();
```

## Concurrent engines

The repository contains experimental `twodog.hosting*` projects and
[hosting demos](https://github.com/outfox/2dog/tree/main/demos) for concurrent
desktop engines. They are not published NuGet packages: each engine needs its
own assembly load context and physical native library copy. CWD, environment
variables and signal handlers remain process-global; macOS hosting is unsupported.
