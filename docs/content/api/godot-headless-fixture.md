# `twodog.fixture.GodotHeadlessFixture`

Starts a Godot test fixture with `--headless`.

```csharp
public class GodotHeadlessFixture : GodotFixtureBase
```

**Package:** `2dog.engine`  
**Namespace:** `twodog.fixture`

This is the usual fixture for game-logic, scene, resource, and CI tests. Use
[`GodotFixture`](./godot-fixture) when a test needs rendering or a window.

## Inherited Properties

| Property | Type | Description |
| --- | --- | --- |
| `Engine` | `twodog.Engine` | Engine owned by the fixture |
| `GodotInstance` | `Godot.GodotInstance` | Running native instance |
| `Tree` | `Godot.SceneTree` | Active scene tree |

## xUnit Collection

[`GodotHeadlessCollection`](./godot-headless-collection) supplies this fixture
to a non-parallel xUnit collection.

```csharp
using twodog.fixture;
using twodog.xunit;
using Xunit;

[Collection<GodotHeadlessCollection>]
public class SceneTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void SceneTree_IsRunning() => Assert.NotNull(godot.Tree.Root);
}
```
