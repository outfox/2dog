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

Sequential restart is supported in packages based on Godot 4.7 and later.
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
