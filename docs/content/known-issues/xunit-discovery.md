---
title: xUnit Test Discovery Crash
description: "Godot values in xUnit MemberData can crash the test host during discovery - the affected Godot types and the workarounds."
---

# xUnit Test Discovery Crash with Godot Types

Godot values in xUnit `[MemberData]` can crash the test host during discovery
([issue #16](https://github.com/outfox/2dog/issues/16)):

```csharp
public static IEnumerable<object[]> Paths = [[new NodePath("/root")]];

[Theory]
[MemberData(nameof(Paths))]
public void CanLogNodePath(NodePath path)
{
    GD.Print(path);
}
```

xUnit enumerates member data before fixtures run. Constructing types that call
Godot native code at that point fails because the engine has not started.

## Workarounds

### Workaround 1: Disable Discovery Enumeration

Defer member-data enumeration until test execution:

```csharp
[Theory]
[MemberData(nameof(Paths), DisableDiscoveryEnumeration = true)]
public void CanLogNodePath(NodePath path)
{
    GD.Print(path);
}
```

The test explorer will show one theory entry rather than one entry per data
row.

### Workaround 2: Use Primitive Values

For separately discoverable rows, pass primitive values and create the Godot
value inside the test after its fixture has started:

```csharp
public static IEnumerable<object[]> Paths = [["/root"], ["/root/Main"]];

[Theory]
[MemberData(nameof(Paths))]
public void CanLogNodePath(string value)
{
    var path = new NodePath(value);
    GD.Print(path);
}
```

Prefer this approach when practical.

## Affected Types

Any GodotSharp type whose construction calls native code may be affected,
including `NodePath`, `StringName`, `Rid`, `GodotObject` subclasses, and some
math value types. Primitive C# values such as strings, numbers, booleans, and
their arrays are safe for discovery.
