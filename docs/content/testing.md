---
title: Testing with xUnit
description: "Run xUnit tests against a real Godot engine with 2dog.xunit: installation, rendering and headless fixtures, shared collections, and writing and running tests."
---

# Testing with xUnit

For NUnit, see [Testing with NUnit](/testing/nunit), which includes signal
expectations, bounded frame waits and a headless fixture base.

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

`2dog.xunit` brings in `2dog.engine` automatically. To generate a test host in
your Godot project, run `dnx 2dog add --tests`. Run the tests with:

```bash
dotnet test MyGame.xunit
```

The generated test project sits inside the Godot project, includes a
`.gdignore`, and points `<GodotProjectDir>` at `..`.
New hosts default to `MyGame.xunit`. Existing `.tests` hosts and custom names
remain supported by `add`, `doctor` and `update`; they are not renamed.

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
        using var scene = GD.Load<PackedScene>("res://test_scene.tscn");
        Assert.NotNull(scene);

        var instance = scene.Instantiate();
        try
        {
            godot.Tree.Root.AddChild(instance);
            Assert.True(instance.IsInsideTree());
        }
        finally { instance.Free(); }
    }
}
```

::: warning Godot types in MemberData
Godot types such as `NodePath` and `StringName` can crash the runner during
discovery. Pass primitive values or set `DisableDiscoveryEnumeration = true`.
See [xUnit Test Discovery](/known-issues/xunit-discovery).
:::

## Async work, signals and deferred deletion

The generated `BasicTests.cs` includes runnable examples of async continuations,
Godot signal awaits, timer signals, signal arguments, entering/leaving the tree,
and `QueueFree`. Every test frees the nodes and resources it owns. Use `Free()`
in `finally` for native nodes; disposing a node's C# wrapper does not free it.

An ordinary `await Task.Delay(...)` preserves the fixture's engine thread but
does not advance Godot. A Godot timer, signal or deferred call needs frames.
Use `AwaitAsync` to pump while an asynchronous operation runs, or
`WaitUntilAsync` to pump until a condition holds:

```csharp
async Task NextFrame()
{
    await godot.Tree.ToSignal(godot.Tree, SceneTree.SignalName.ProcessFrame);
}

await godot.AwaitAsync(NextFrame(), cancellationToken: TestContext.Current.CancellationToken);
await godot.WaitUntilAsync(() => node.IsNodeReady(),
    timeout: TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
```

Both helpers default to a five-second timeout, observe xUnit cancellation, and
keep pumping on the engine thread. Task results, exceptions and cancellation
propagate through `AwaitAsync`. A timeout stops waiting; it cannot cancel the
operation you supplied, so cancel or clean up that operation yourself.
Await one pumping operation at a time. Do not use `ConfigureAwait(false)` or
access Godot objects from `Task.Run`.

### Expect signals

Create an expectation **before** triggering the behavior. This catches both
synchronous and deferred emissions:

```csharp
using var entered = GodotAssert.ExpectSignal<Node>(parent, Node.SignalName.ChildEnteredTree);
parent.AddChild(child);
entered.AssertEmitted(); // Exactly once; AssertEmitted(2) checks two emissions.
Assert.Same(child, Assert.Single(entered.Values));

using var timeout = GodotAssert.ExpectSignal(timer, Godot.Timer.SignalName.Timeout);
timer.Start();
await timeout.WaitAsync(godot, cancellationToken: TestContext.Current.CancellationToken);
timeout.AssertEmitted();
```

`ExpectSignal` handles signals without arguments; `ExpectSignal<T>` records a
single argument in `Values`, in emission order. `WaitAsync` advances frames until
at least one emission arrives (or pass `count:`). It fails with the signal name
on timeout or if the source is freed first. Expectations disconnect on disposal,
timeout and cancellation; disposal itself does not assert. For other signal
signatures, connect a typed callable and use `WaitUntilAsync` on a recorded
condition.

### Expect deletion

`QueueFree` marks a node for deletion; it is still valid until Godot flushes the
queue. Check actual deletion with:

```csharp
node.QueueFree();
Assert.True(node.IsQueuedForDeletion());
await GodotAssert.FreedAsync(godot, node, cancellationToken: TestContext.Current.CancellationToken);
Assert.False(GodotObject.IsInstanceValid(node));
```

`TreeExiting` fires while the node is still inside the tree; `TreeExited` fires
after removal. Removing a node from its parent does not free it, so tree exit
and deletion should be asserted separately.

## Simulating Input

The shared fixture exposes `godot.Input`. Aim at a control through Godot's
viewport router, preserving foreground controls, clipping and input suppression:

```csharp
godot.Engine.Iteration(); // Settle layout first.
using var pressed = GodotAssert.ExpectSignal(playButton, BaseButton.SignalName.Pressed);
godot.Input.Click(playButton);
pressed.AssertEmitted();
```

`PushToViewport` routes custom events locally; `Send` uses Godot's input server
and updates global polling state. See [Simulating Input](/testing/input) for
keyboard focus, coordinate-based clicks and the distinction between modes.

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

On macOS that thread is the process main thread, because AppKit aborts a
windowed engine started anywhere else. `2dog.xunit` wraps the entry point xUnit
generates so that xUnit's runner moves to another thread and the main thread
runs the 2dog collections, one at a time. A test project with its own entry
point should run xUnit through `GodotTestThread.RunMain`.

Tests without a 2dog fixture keep xUnit's usual threads. To use a different
xUnit test framework, set
`<TwoDogGodotTestThread>false</TwoDogGodotTestThread>`. That leaves out the
entry point too, so on macOS windowed fixtures abort unless your framework runs
their collections through `GodotTestThread.Run`, from an entry point that calls
`GodotTestThread.RunMain`.

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
