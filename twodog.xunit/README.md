# 2dog.xunit

xUnit collection definitions for testing Godot applications with [2dog](https://github.com/outfox/2dog).

The fixtures themselves (`GodotFixture`, `GodotHeadlessFixture`, `GodotFixtureBase`) ship in the
**`2dog`** package, in the `twodog.fixture` namespace. This package adds the xUnit-specific glue on
top of them.

## What it provides

- **`GodotCollection`**  –  binds the full (rendering) `GodotFixture`
- **`GodotHeadlessCollection`**  –  binds `GodotHeadlessFixture` (use this for CI)

Both set `DisableParallelization = true`, which is required because Godot allows only one instance
per process.

## How it works

xUnit only discovers `[CollectionDefinition]` classes that live in the **test assembly**  –  a
definition shipped in a referenced DLL is silently ignored (its `DisableParallelization` and
`ICollectionFixture<T>` are not applied). To make the collections actually work, this package ships
them as **compile-in source**: a `build/2dog.xunit.targets` file adds the collection definitions to
your test project's compilation, so they end up in your test assembly where xUnit can find them.

You therefore do **not** write your own collection definition  –  just reference the package and use
the collections directly.

## Usage

```csharp
using Godot;
using twodog.fixture; // GodotHeadlessFixture
using twodog.xunit;   // GodotHeadlessCollection
using Xunit;

[Collection<GodotHeadlessCollection>]
public class MyTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void EngineStarts()
    {
        Assert.NotNull(godot.Tree);
    }
}
```

## Custom fixtures

Need a different Godot configuration? Subclass `GodotFixtureBase` (from the `2dog` package) and
write a one-line collection for it in your own test project:

```csharp
using twodog.fixture;
using Xunit;

public class GodotOpenGl3Fixture() : GodotFixtureBase("--display-driver", "opengl3");

[CollectionDefinition(nameof(GodotOpenGl3Collection), DisableParallelization = true)]
public class GodotOpenGl3Collection : ICollectionFixture<GodotOpenGl3Fixture>;
```
