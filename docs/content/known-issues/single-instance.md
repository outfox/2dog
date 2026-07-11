# Single Godot Instance At A Time

Only one Godot instance can exist per process **at a time**. Attempting to
start a second instance while one is running throws an
`InvalidOperationException`:

```csharp
using var engine1 = new Engine("app1", "./project");
using var godot1 = engine1.Start();

using var engine2 = new Engine("app2", "./project");
using var godot2 = engine2.Start(); // Throws InvalidOperationException
```

**Sequential restart is supported** (since the Godot 4.7 based packages):
once an instance is disposed, a new one can be created in the same process:

```csharp
var engine = new Engine("app", "./project");
var godot = engine.Start();

godot.Dispose();
engine.Dispose();

// Works: the previous instance was destroyed first.
using var engine2 = new Engine("app", "./project");
using var godot2 = engine2.Start();
```

This is what allows multiple xUnit test collections  –  each with its own
fixture  –  to run sequentially in one test process. See
[Testing](../testing).

If you need multiple Godot environments running **concurrently**, you must
still use separate processes  –  that remains a fundamental constraint of the
Godot engine architecture (global singletons).
