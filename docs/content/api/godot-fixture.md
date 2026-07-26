# `twodog.fixture.GodotFixture`

Starts a Godot test fixture with rendering enabled.

```csharp
public class GodotFixture : GodotFixtureBase
```

**Package:** `2dog.engine`  
**Namespace:** `twodog.fixture`

Use this fixture for tests that need a display, rendered frames, or window
behavior. For most CI tests, prefer [`GodotHeadlessFixture`](./godot-headless-fixture).

## Inherited Properties

| Property | Type | Description |
| --- | --- | --- |
| `Engine` | `twodog.Engine` | Engine owned by the fixture |
| `GodotInstance` | `Godot.GodotInstance` | Running native instance |
| `Tree` | `Godot.SceneTree` | Active scene tree |

## xUnit Collection

[`GodotCollection`](./godot-collection) supplies this fixture to a
non-parallel xUnit collection.

```csharp
using twodog.fixture;
using twodog.xunit;
using Xunit;

[Collection<GodotCollection>]
public class RenderingTests(GodotFixture godot)
{
    [Fact]
    public void SceneTree_IsRunning() => Assert.NotNull(godot.Tree.Root);
}
```
