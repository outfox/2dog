# `twodog.xunit.GodotCollection`

Defines a non-parallel xUnit collection backed by [`GodotFixture`](./godot-fixture).

```csharp
public class GodotCollection : ICollectionFixture<GodotFixture>
```

**Package:** `2dog.xunit`  
**Namespace:** `twodog.xunit`

The package compiles this collection definition into your test assembly so
xUnit can discover it.

## Usage

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

Tests in the collection share one fixture. Clean up nodes and other state that
should not carry into the next test.
