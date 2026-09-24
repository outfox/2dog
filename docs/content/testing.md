---
title: Testing with xUnit
description: "Run xUnit tests against a real Godot engine with 2dog.xunit: installation, rendering and headless fixtures, shared collections, and writing and running tests."
---

# Testing with xUnit

`2dog.engine` provides the fixtures in `twodog.Testing`. `2dog.xunit` adds
ready-made xUnit collections, so tests can start a real Godot engine without
having to manage its lifetime themselves.

## Installation

```bash
dotnet add package 2dog.xunit
dotnet add package xunit.v3
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit.runner.visualstudio
```

`2dog.xunit` brings in `2dog.engine` automatically. Projects created by the
[`2dog` tool](/add) already include a test project; add one later with
`2dog add --tests`. Run it with:

```bash
dotnet test MyGame.tests
```

The generated test project sits inside the Godot project, includes a
`.gdignore`, and points `<GodotProjectDir>` at `..`.

## Fixtures

Both fixtures derive from `FixtureBase`, which starts the engine in its
constructor and exposes the objects most tests need:

```csharp
public abstract class FixtureBase : IDisposable
{
    protected FixtureBase(params string[] cmdLineArgs);

    public Engine Engine { get; }
    public GodotInstance GodotInstance { get; }
    public SceneTree Tree { get; }
}
```

- `Fixture` starts Godot with rendering enabled.
- `HeadlessFixture` adds `--headless` and is the usual choice for CI.

## Collections

A Godot instance is not thread-safe. Put engine tests in a collection with
`DisableParallelization = true`; tests in that collection share one fixture.

`2dog.xunit` ships `RenderingCollection` and `HeadlessCollection`. Their
collection definitions are compiled into your test assembly because xUnit
does not discover definitions from an ordinary referenced DLL.

```csharp
using twodog.Testing;
using twodog.Testing.Xunit;

[Collection<HeadlessCollection>]
public class MyTests(HeadlessFixture godot)
{
    // Tests share godot.Engine, godot.GodotInstance, and godot.Tree.
}
```

You may use several non-parallel Godot collections. xUnit disposes one
collection fixture before starting the next, giving each collection a fresh
engine instance.

### Custom Collections

For different Godot arguments, derive from `FixtureBase` and define the
collection in your test project:

```csharp
using twodog.Testing;
using Xunit;

public class OpenGl3Fixture()
    : FixtureBase("--rendering-driver", "opengl3");

[CollectionDefinition(nameof(OpenGl3Collection), DisableParallelization = true)]
public class OpenGl3Collection : ICollectionFixture<OpenGl3Fixture>;
```

See [Single Godot Instance](/known-issues/single-instance) for the engine
lifetime constraint.

## Writing Tests

```csharp
using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit;

[Collection<HeadlessCollection>]
public class SceneTests(HeadlessFixture godot)
{
    [Fact]
    public void LoadScene_ValidPath_Succeeds()
    {
        var scene = GD.Load<PackedScene>("res://test_scene.tscn");
        Assert.NotNull(scene);

        var instance = scene.Instantiate();
        godot.Tree.Root.AddChild(instance);

        Assert.True(instance.IsInsideTree());
        instance.QueueFree();
    }
}
```

::: warning Godot types in MemberData
Godot types such as `NodePath` and `StringName` can crash the runner during
discovery. Pass primitive values or set `DisableDiscoveryEnumeration = true`.
See [xUnit Test Discovery](/known-issues/xunit-discovery).
:::

## Godot Errors

Any error or warning Godot reports while a test runs fails that test. That
covers `push_error`, failed engine checks, and exceptions Godot catches in C#
callbacks, which it would otherwise only print. A test that expects an error
consumes it through the fixture, which also checks its text:

```csharp
[Fact]
public void RejectsNegativeHealth()
{
    player.Health = -1;
    godot.Errors.Expect("Health must not be negative");
}
```

Errors reported between tests, such as during engine startup or from deferred
calls an earlier test left queued, fail the next test on a fixture and say so.
Tests without a 2dog fixture are not checked.

To opt out, mark a test or class `[AllowGodotErrors]`, or set
`<TwoDogFailOnGodotErrors>false</TwoDogFailOnGodotErrors>` in the test project.

## Godot's Thread

Each collection that uses a 2dog fixture runs on a thread of its own. The
engine starts there, and the tests, their `await` continuations, and the
fixture's disposal all stay on it, as Godot requires. Between tests the thread
also runs continuations Godot queued for its frame loop. On Windows the thread
is STA, which Godot's windowing needs for drag-and-drop. `2dog.xunit` also
gives test projects the comctl32 v6 manifest that `godot.exe` has, unless they
set their own `ApplicationManifest`.

Tests without a 2dog fixture keep xUnit's usual threads. To use a different
xUnit test framework, set
`<TwoDogGodotTestThread>false</TwoDogGodotTestThread>`.

## Running Tests

```bash
dotnet test
dotnet test -c Release                            # or Debug / Editor
dotnet test --logger "console;verbosity=detailed"
dotnet test --filter "FullyQualifiedName~SceneTests"
```

Each configuration selects the matching engine variant; see
[Build Variants](./build-configurations). Asset import runs automatically when
the test project builds and does not require the Editor configuration; see
[Resource Import](./import-tool).

For headless CI, select the headless collection and use a dummy audio driver:

```yaml
- name: Run tests
  run: dotnet test --configuration Release
  env:
    GODOT_AUDIO_DRIVER: Dummy
```
