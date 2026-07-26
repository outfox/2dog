# `twodog.Testing.Xunit.RenderingCollection`

Defines a non-parallel xUnit collection backed by [`Fixture`](./fixture).

```csharp
public class RenderingCollection : ICollectionFixture<Fixture>
```

**Package:** `2dog.xunit`  
**Namespace:** `twodog.Testing.Xunit`

The package compiles this collection definition into your test assembly so
xUnit can discover it.

## Usage

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

Tests in the collection share one fixture. Clean up nodes and other state that
should not carry into the next test.
