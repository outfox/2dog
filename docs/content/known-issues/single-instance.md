---
title: Single Godot Instance
description: "Only one Godot instance may run per assembly load context in 2dog; sequential restart is supported and isolated concurrent hosting is experimental."
---

# Single Godot Instance Per Load Context

Only one Godot instance may run in an assembly load context at a time. Starting
a second instance throws `InvalidOperationException`:

```csharp
using var engine1 = new Engine("MyGame");
using var godot1 = engine1.Start();

using var engine2 = new Engine("MyGame");
using var godot2 = engine2.Start(); // Throws InvalidOperationException.
```

Sequential restart is supported in packages based on Godot 4.7 and later,
including the browser: the [Blazor host](/hosts/blazor) restarts on a fresh
canvas, and a web host that keeps the runtime alive
(`Engine.WebExitRuntimeOnQuit = false`) can start a new engine after `Exited`.
Dispose both the running instance and its engine before starting another:

```csharp
var engine = new Engine("MyGame");
var godot = engine.Start();

godot.Dispose();
engine.Dispose();

using var nextEngine = new Engine("MyGame");
using var nextGodot = nextEngine.Start();
```

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
