# 2dog.xunit

xUnit test fixtures for testing Godot applications built with [2dog](https://github.com/outfox/2dog).

## Fixtures

- **`GodotFixture`** - Full Godot fixture with rendering support
- **`GodotHeadlessFixture`** - Headless fixture for CI/automated testing

## Usage

```csharp
using twodog.xunit;

[CollectionDefinition("Godot", DisableParallelization = true)]
public class GodotCollection : ICollectionFixture<GodotHeadlessFixture>;

[Collection("Godot")]
public class MyTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void EngineStarts()
    {
        Assert.NotNull(godot.Engine.Tree);
    }
}
```

Note: Only one Godot instance can exist per process. Use `DisableParallelization = true` on your collection definition.
