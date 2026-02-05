# xUnit Test Discovery Crash with Godot Types

Using Godot types in xUnit `[MemberData]` will crash the test runner during discovery.

## The Problem

When you use Godot types like `NodePath`, `StringName`, `Vector2`, `Color`, etc. in `[MemberData]`, the test runner crashes:

```csharp
[Collection("GodotHeadless")]
public class BasicTests(GodotHeadlessFixture godot)
{
    public static IEnumerable<object[]> paths = [[new NodePath("/root")]];

    [Theory]
    [MemberData(nameof(paths))]
    public void CanLog_NodePath(NodePath path)
    {
        GD.Print(path);
    }
}
```

Running `dotnet test` produces:

```
[xUnit.net 00:00:00.04]   Discovering: MyGame.Tests
The active test run was aborted. Reason: Test host process crashed
```

## Why This Happens

xUnit enumerates `MemberData` during test discovery to display test cases. This instantiates Godot types (like `new NodePath("/root")`) before any test runs. Since the `GodotFixture` hasn't started the engine yet, GodotSharp tries to call into native code that doesn't exist, causing a crash.

## Workaround 1: Disable Discovery Enumeration

Add `DisableDiscoveryEnumeration = true` to your `MemberData` attribute:

```csharp
[Theory]
[MemberData(nameof(paths), DisableDiscoveryEnumeration = true)]
public void CanLog_NodePath(NodePath path)
{
    GD.Print(path);
}
```

This prevents xUnit from enumerating the data during discovery, deferring instantiation until test execution when Godot is running.

::: warning
With `DisableDiscoveryEnumeration = true`, the test explorer will show a single entry for the theory instead of individual entries for each test case.
:::

## Workaround 2: Use Primitive Types

Pass primitive types (strings, numbers) in `MemberData` and construct Godot objects inside the test:

```csharp
public static IEnumerable<object[]> paths = [["/root"], ["/root/Main"]];

[Theory]
[MemberData(nameof(paths))]
public void CanLog_NodePath(string pathStr)
{
    var path = new NodePath(pathStr);
    GD.Print(path);
}
```

This approach keeps test cases visible in the test explorer while avoiding the discovery crash.

## Advanced: ModuleInitializer Approach

If you need Godot types directly in `MemberData` without `DisableDiscoveryEnumeration`, you can initialize Godot before test discovery using a module initializer. This ensures the engine is running before xUnit enumerates your test data.

::: danger Warning
ModuleInitializers run automatically when an assembly loads, which has hidden side effects and can cause subtle issues. The `DisableDiscoveryEnumeration` workaround above is simpler and recommended for most cases.
:::

Create a file in your test project (e.g., `TestInitializer.cs`):

```csharp
using System.Runtime.CompilerServices;
using twodog;

namespace MyGame.Tests;

internal static class TestInitializer
{
    private static Engine? _engine;
    private static GodotInstance? _godot;

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Prevent double-initialization
        if (_engine != null) return;

        _engine = new Engine("tests", "../game", "--headless");
        _godot = _engine.Start();

        // Clean up when the process exits
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _godot?.Dispose();
            _engine?.Dispose();
        };
    }
}
```

With this in place, Godot types can be used directly in `MemberData`:

```csharp
public static IEnumerable<object[]> paths = [[new NodePath("/root")]];

[Theory]
[MemberData(nameof(paths))]  // No DisableDiscoveryEnumeration needed
public void CanLog_NodePath(NodePath path)
{
    GD.Print(path);
}
```

::: warning Caveats
- **Slower discovery**: The engine starts during test discovery, slowing down IDE test explorers
- **Cleanup via ProcessExit**: Less elegant than proper `IDisposable` patterns used by fixtures
- **Must be in your test project**: Cannot be placed in `twodog.xunit` since module initializers only run for the assembly they're defined in
- **Conflicts with fixtures**: If you use both this approach and `GodotFixture`/`GodotHeadlessFixture`, you'll get an `InvalidOperationException` (only one Godot instance per process)
:::

## Affected Types

Any GodotSharp type that makes native calls in its constructor is affected. Common examples:

- `NodePath`
- `StringName`
- `Vector2`, `Vector3`, `Vector4`
- `Color`
- `Rid`
- `Transform2D`, `Transform3D`
- Any `GodotObject` subclass

Primitive C# types (`string`, `int`, `float`, `bool`, arrays of primitives) are safe to use directly.
