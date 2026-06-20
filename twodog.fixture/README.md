# 2dog.fixture

[2dog](https://github.com/outfox/2dog) unit test fixture for testing Godot applications.

## Fixtures

- **`GodotFixture`** - Full Godot fixture with rendering support
- **`GodotHeadlessFixture`** - Headless fixture for CI/automated testing
- **`GodotFixtureBase`** - Base class for creating your own fixture

## Usage with xUnit

```csharp
using twodog.fixture;

// Fixture Collections
[CollectionDefinition("GodotHeadless", DisableParallelization = true)]
public class GodotHeadlessCollection : ICollectionFixture<GodotHeadlessFixture>;

// Tests
[Collection("GodotHeadless")]
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

## Extending GodotFixtureBase

Extending `GodotFixtureBase` is easy as pie, all you do is inherit the class and specify the [godot compiler arguments](https://docs.godotengine.org/en/latest/tutorials/editor/command_line_tutorial.html) you want to use.

```csharp
public class GodotOpenGl3Fixture() : GodotFixtureBase("--display-driver opengl3");
```