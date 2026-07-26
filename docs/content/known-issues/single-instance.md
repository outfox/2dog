# Single Godot Instance Per Load Context

Only one Godot instance may run in an assembly load context at a time. Starting
a second instance throws `InvalidOperationException`:

```csharp
using var engine1 = new Engine("app1", "./project");
using var godot1 = engine1.Start();

using var engine2 = new Engine("app2", "./project");
using var godot2 = engine2.Start(); // Throws InvalidOperationException.
```

Sequential restart is supported in packages based on Godot 4.7 and later.
Dispose both the running instance and its engine before starting another:

```csharp
var engine = new Engine("app", "./project");
var godot = engine.Start();

godot.Dispose();
engine.Dispose();

using var nextEngine = new Engine("app", "./project");
using var nextGodot = nextEngine.Start();
```

This allows xUnit collections to use fresh engines sequentially in one test
process. Collections that share an engine must disable parallelization; see
[Testing](../testing).

Isolated assembly load contexts can host concurrent engines, but the current
path is experimental and retains process-wide limits. See
[Miscellaneous Advanced Notes](/misc#experimental-parallel-xunit-engines).
