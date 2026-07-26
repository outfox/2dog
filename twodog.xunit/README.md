# 2dog.xunit

xUnit collection definitions for testing Godot applications with [2dog](https://github.com/outfox/2dog).

The fixtures themselves (`Fixture`, `HeadlessFixture`, and `FixtureBase`) ship in the
**`2dog.engine`** package under `twodog.Testing`. This package adds the xUnit-specific glue.

## What it provides

- **`RenderingCollection`** - binds the rendering-enabled `Fixture`
- **`HeadlessCollection`** - binds `HeadlessFixture` (use this for CI)

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
using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit;

[Collection<HeadlessCollection>]
public class MyTests(HeadlessFixture godot)
{
    [Fact]
    public void EngineStarts()
    {
        Assert.NotNull(godot.Tree);
    }
}
```

## Custom fixtures

Need a different Godot configuration? Subclass `FixtureBase` and
write a one-line collection for it in your own test project:

```csharp
using twodog.Testing;
using Xunit;

public class OpenGl3Fixture() : FixtureBase("--rendering-driver", "opengl3");

[CollectionDefinition(nameof(OpenGl3Collection), DisableParallelization = true)]
public class OpenGl3Collection : ICollectionFixture<OpenGl3Fixture>;
```
