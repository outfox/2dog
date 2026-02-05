# Known Issues

This page documents known limitations and their workarounds.

## Single Godot Instance Per Process

Only one Godot instance can exist per process. Attempting to start a second instance will throw an `InvalidOperationException`.

```csharp
using var engine1 = new Engine("app1", "./project");
using var godot1 = engine1.Start();

using var engine2 = new Engine("app2", "./project");
using var godot2 = engine2.Start(); // Throws InvalidOperationException
```

This is a fundamental constraint of the Godot engine architecture. If you need multiple isolated Godot environments, you must use separate processes.

## xUnit Test Discovery Crash with Godot Types

Using Godot types in xUnit `[MemberData]` will crash the test runner during discovery.

### The Problem

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

### Why This Happens

xUnit enumerates `MemberData` during test discovery to display test cases. This instantiates Godot types (like `new NodePath("/root")`) before any test runs. Since the `GodotFixture` hasn't started the engine yet, GodotSharp tries to call into native code that doesn't exist, causing a crash.

### Workaround 1: Disable Discovery Enumeration

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

### Workaround 2: Use Primitive Types

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

### Affected Types

Any GodotSharp type that makes native calls in its constructor is affected. Common examples:

- `NodePath`
- `StringName`
- `Vector2`, `Vector3`, `Vector4`
- `Color`
- `Rid`
- `Transform2D`, `Transform3D`
- Any `GodotObject` subclass

Primitive C# types (`string`, `int`, `float`, `bool`, arrays of primitives) are safe to use directly.
