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

xUnit only discovers `[CollectionDefinition]` classes that live in the **test assembly** – a
definition shipped in a referenced DLL is silently ignored (its `DisableParallelization` and
`ICollectionFixture<T>` are not applied). To make the collections actually work, this package ships
them as **compile-in source**: a `build/2dog.xunit.targets` file adds the collection definitions to
your test project's compilation, so they end up in your test assembly where xUnit can find them.

You therefore do **not** write your own collection definition – just reference the package and use
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

## Godot's thread

Each collection that uses a 2dog fixture runs on a thread of its own: the engine starts there, and the tests,
their `await` continuations, and the fixture's disposal stay on it, as Godot requires. Between tests, the thread
also runs the continuations Godot queued for its frame loop. On Windows the thread is STA, which Godot's windowing
needs for OLE drag-and-drop. Tests without a 2dog fixture keep xUnit's usual threads.

This is done by a test framework registered for the test assembly. If you need your own, set
`<TwoDogGodotTestThread>false</TwoDogGodotTestThread>`.

The package also gives test projects the comctl32 v6 application manifest that `godot.exe` has, unless the project
sets its own `ApplicationManifest`. Without it, Godot's Windows display server warns about common controls.

## Godot errors fail tests

Every error and warning Godot reports during a test fails that test, including `push_error`, failed engine
checks, and exceptions Godot catches in C# callbacks (which it otherwise only prints). This applies to every
test that runs on a 2dog fixture; plain unit tests without an engine are left alone.

A test that expects an error consumes it, which also asserts its text:

```csharp
[Fact]
public void RejectsNegativeHealth()
{
    player.Health = -1;
    godot.Errors.Expect("Health must not be negative");
}
```

Errors reported between tests (engine startup, or deferred work an earlier test left behind) fail the next
test that runs on a fixture, with a message saying so.

To opt out, mark a test or class `[AllowGodotErrors]`, or turn the check off for the whole project:

```xml
<PropertyGroup>
    <TwoDogFailOnGodotErrors>false</TwoDogFailOnGodotErrors>
</PropertyGroup>
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
