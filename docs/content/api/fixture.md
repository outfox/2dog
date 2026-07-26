# `twodog.Testing.Fixture`

Starts a Godot test fixture with rendering enabled.

```csharp
public class Fixture : FixtureBase
```

**Package:** `2dog.engine`  
**Namespace:** `twodog.Testing`

Use this fixture for tests that need a display, rendered frames, or window
behavior. For most CI tests, prefer [`HeadlessFixture`](./headless-fixture).

## Inherited Properties

| Property | Type | Description |
| --- | --- | --- |
| `Engine` | `twodog.Engine` | Engine owned by the fixture |
| `GodotInstance` | `Godot.GodotInstance` | Running native instance |
| `Tree` | `Godot.SceneTree` | Active scene tree |

## xUnit Collection

[`RenderingCollection`](./rendering-collection) supplies this fixture to a
non-parallel xUnit collection.

```csharp
using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit;

[Collection<RenderingCollection>]
public class RenderingTests(Fixture godot)
{
    [Fact]
    public void SceneTree_IsRunning() => Assert.NotNull(godot.Tree.Root);
}
```
