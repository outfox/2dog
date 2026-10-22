---
title: xUnit Test Discovery Crash
description: "Godot values in xUnit MemberData can crash the test host during discovery - the affected Godot types and the workarounds."
tagline: "Test host crashing during discovery? Learn how to fix it."
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

xUnit enumerates member data before fixtures run. At that point, the engine has
not started yet, so constructors that call Godot native code fail.

## Workarounds

### Workaround 1: Disable Discovery Enumeration

Set `DisableDiscoveryEnumeration = true` so that xUnit enumerates the member
data only when the tests run:

```csharp
[Theory]
[MemberData(nameof(Paths), DisableDiscoveryEnumeration = true)]
public void CanLogNodePath(NodePath path)
{
    GD.Print(path);
}
```

The test explorer then shows one theory entry, not one entry for each data
row.

### Workaround 2: Use Primitive Values

If you need one test explorer entry for each row, pass primitive values. Then
create the Godot value inside the test, after its fixture has started:

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

Prefer this workaround.

## Affected Types

Any GodotSharp type whose constructor calls native code is affected:
`NodePath`, `StringName`, `Rid`, `GodotObject` subclasses, and some math
value types. Primitive C# values such as strings, numbers, booleans, and
their arrays are safe for discovery.
