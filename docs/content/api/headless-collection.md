# `twodog.Testing.Xunit.HeadlessCollection`

Defines a non-parallel xUnit collection backed by
[`HeadlessFixture`](./headless-fixture).

```csharp
public class HeadlessCollection : ICollectionFixture<HeadlessFixture>
```

**Package:** `2dog.xunit`  
**Namespace:** `twodog.Testing.Xunit`

The package compiles this collection definition into your test assembly so
xUnit can discover it. This is the usual collection for CI.

## Usage

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

Tests in the collection share one fixture. Clean up nodes and other state that
should not carry into the next test.
