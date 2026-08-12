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
If you create one manually, copy the required project setup from
[xUnit Host](/hosts/xunit#project-differences).

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
