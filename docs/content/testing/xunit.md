---
title: Testing with xUnit
description: "Write your first Godot test with xUnit and choose a shared engine fixture."
---

# Testing with xUnit

Add a test project from your Godot project directory:

```bash
dnx 2dog add --xunit
dotnet test MyGame.xunit
```

The generated `BasicTests.cs` has runnable examples. Replace or extend them
with tests for your game.

## Your first test

Put engine tests in a `HeadlessCollection` and take its fixture as a constructor
argument. This test adds a node to the scene tree and checks that it entered:

```csharp
using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit;

[Collection<HeadlessCollection>]
public class SceneTests(HeadlessFixture godot)
{
    [Fact]
    public void NodeEntersTree()
    {
        var node = new Node();
        try
        {
            godot.Tree.Root.AddChild(node);
            Assert.True(node.IsInsideTree());
        }
        finally { node.Free(); }
    }
}
```

The fixture exposes `Engine`, `GodotInstance`, `Tree`, `Input`, and `Errors`.
Tests in the collection share these objects, so clean up what each test creates.

## Choose a fixture

| Collection | Constructor argument | Use |
| --- | --- | --- |
| `HeadlessCollection` | `HeadlessFixture godot` | Scene logic and CI, without a window |
| `RenderingCollection` | `Fixture godot` | Tests that need rendering |

Keep Godot collections non-parallel. You can use several; xUnit disposes one
collection's engine before starting the next.

::: details Custom engine arguments
Derive from `FixtureBase` and define a non-parallel collection in your test
project:

```csharp
using twodog.Testing;
using Xunit;

public class OpenGl3Fixture()
    : FixtureBase("--rendering-driver", "opengl3");

[CollectionDefinition(nameof(OpenGl3Collection), DisableParallelization = true)]
public class OpenGl3Collection : ICollectionFixture<OpenGl3Fixture>;
```

The built-in collection definitions compile into your test assembly because
xUnit does not discover them in an ordinary referenced DLL.
:::

## Waits, signals, and input

Use [Writing engine tests](./writing-tests) for frame waits, signal expectations,
cleanup, and expected errors. Use [Simulating input](./input) for UI tests.
Both guides work with xUnit and NUnit.

::: warning Test data runs before the engine
Godot types such as `NodePath` and `StringName` in `MemberData` can crash
discovery. Pass primitive values or set `DisableDiscoveryEnumeration = true`.
See [xUnit test discovery](/known-issues/xunit-discovery).
:::

## Runner settings

2dog keeps the tests, async continuations, and fixture disposal on the engine's
owner thread. Keep Godot access on that thread; avoid `Task.Run` and
`ConfigureAwait(false)` around it.

::: details Custom entry points and test frameworks
On Windows, the collection thread is STA. 2dog also supplies the comctl32 v6
manifest unless you set your own `ApplicationManifest`.

On macOS, windowed Godot needs the process main thread. 2dog wraps xUnit's
generated entry point to run Godot collections there. A project with its own
entry point should run xUnit through `GodotTestThread.RunMain`.

To use another xUnit test framework, set
`<TwoDogGodotTestThread>false</TwoDogGodotTestThread>`. This also removes the
entry-point wrapper. For windowed macOS fixtures, your entry point must call
`GodotTestThread.RunMain` and your framework must run collections through
`GodotTestThread.Run`.
:::

::: details Adding packages by hand
The scaffold sets this up for you. For an existing test project:

```bash
dotnet add package 2dog.xunit
dotnet add package xunit.v3
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit.runner.visualstudio
```

`2dog.xunit` brings in `2dog.engine`. See
[xUnit configuration](/configuration/xunit) for game paths and Editor bindings.
:::
