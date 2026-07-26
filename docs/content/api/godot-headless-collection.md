# `twodog.xunit.GodotHeadlessCollection`

Defines a non-parallel xUnit collection backed by
[`GodotHeadlessFixture`](./godot-headless-fixture).

```csharp
public class GodotHeadlessCollection : ICollectionFixture<GodotHeadlessFixture>
```

**Package:** `2dog.xunit`  
**Namespace:** `twodog.xunit`

The package compiles this collection definition into your test assembly so
xUnit can discover it. This is the usual collection for CI.

## Usage

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

Tests in the collection share one fixture. Clean up nodes and other state that
should not carry into the next test.
