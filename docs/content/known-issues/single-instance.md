# Single Godot Instance Per Load Context

Only one Godot instance can exist **per assembly load context at a time**.
Attempting to start a second instance while one is running throws an
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

## Concurrent instances via 2dog.hosting

Multiple Godot engines **running concurrently in one process** are possible
through the `twodog.hosting` orchestrator: each instance gets its own physical
copy of the native libgodot (pooled under `%LOCALAPPDATA%/2dog/native-pool`)
and its own `AssemblyLoadContext` for the managed engine stack, so the
"one instance" rule  –  which is really per native module and per load
context  –  applies to each instance independently:

```csharp
var host = new EngineHost();
using var a = host.Start<MyProgram>(new() { Tag = "A", ProjectDir = "/abs/projA" });
using var b = host.Start<MyProgram>(new() { Tag = "B", ProjectDir = "/abs/projB" });
await Task.WhenAll(a.Completion, b.Completion);
```

The program type runs inside the instance's load context with the ordinary
single-instance programming model (`new Engine(...) { NativePath = ctx.NativePath }`,
`Start()`, pump). Godot types never cross the context boundary  –  only
CoreLib types do.

Limits that remain process-global and cannot be isolated in-process: the
current working directory (the engine moves it during boot  –  always pass
absolute paths), environment variables, native crash blast radius,
signal/exception handlers, and stdio. `user://` collides across instances
whose projects share `application/config/name`. On macOS in-process hosting
is not yet supported; for full isolation, one process per engine remains the
recommendation.
