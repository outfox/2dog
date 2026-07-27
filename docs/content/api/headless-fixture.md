---
title: twodog.Testing.HeadlessFixture
description: "API reference for HeadlessFixture, the test fixture that starts Godot with --headless - used through HeadlessCollection in xUnit tests."
---

# `twodog.Testing.HeadlessFixture`

Starts a Godot test fixture with `--headless`.

```csharp
public class HeadlessFixture : FixtureBase
```

**Package:** `2dog.engine`  
**Namespace:** `twodog.Testing`

This is the usual fixture for game-logic, scene, resource, and CI tests. Use
[`Fixture`](./fixture) when a test needs rendering or a window.

## Inherited Properties

| Property | Type | Description |
| --- | --- | --- |
| `Engine` | `twodog.Engine` | Engine owned by the fixture |
| `GodotInstance` | `Godot.GodotInstance` | Running native instance |
| `Tree` | `Godot.SceneTree` | Active scene tree |

## xUnit Collection

[`HeadlessCollection`](./headless-collection) supplies this fixture to a
non-parallel xUnit collection.

```csharp
using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit;

[Collection<HeadlessCollection>]
public class SceneTests(HeadlessFixture godot)
{
    [Fact]
    public void SceneTree_IsRunning() => Assert.NotNull(godot.Tree.Root);
}
```
