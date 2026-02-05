# Single Godot Instance Per Process

Only one Godot instance can exist per process. Attempting to start a second instance will throw an `InvalidOperationException`.

```csharp
using var engine1 = new Engine("app1", "./project");
using var godot1 = engine1.Start();

using var engine2 = new Engine("app2", "./project");
using var godot2 = engine2.Start(); // Throws InvalidOperationException
```

Additionally, once an instance is disposed, it cannot be restarted or re-initialized within the same process:

```csharp
var engine = new Engine("app", "./project");
var godot = engine.Start();

godot.Dispose();
engine.Dispose();

// Cannot create a new instance - the process is "tainted"
var engine2 = new Engine("app", "./project");
var godot2 = engine2.Start(); // Throws InvalidOperationException
```

These are fundamental constraints of the Godot engine architecture. If you need multiple isolated Godot environments or need to restart the engine, you must use separate processes.
