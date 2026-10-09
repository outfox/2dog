---
title: Testing with NUnit
description: "Write NUnit tests with a real headless Godot engine, signal expectations, simulated input and native error checking."
---

# Testing with NUnit

The [NUnit host](/hosts/nunit) uses the NUnit.org framework and a real headless
Godot engine. Scaffold a test project beside your game:

```bash
dnx 2dog add --nunit
dotnet test MyGame.nunit
```

The generated project references `2dog.nunit`, `NUnit`, `NUnit3TestAdapter`
and `Microsoft.NET.Test.Sdk`. It includes a `.gdignore` and sets
`GodotProjectDir` to `..`. For package and variant settings, see
[NUnit Configuration](/configuration/nunit).

## Fixture Lifetime

Derive each test class from `GodotTestFixture` in `twodog.Testing.NUnit`.
Its protected `Godot` property exposes the shared engine fixture, scene tree,
error log and input simulator. Each derived fixture gets one headless engine.

```csharp
using Godot;
using NUnit.Framework;
using twodog.Testing.NUnit;

public class MenuTests : GodotTestFixture
{
    [Test]
    public void ClickingPlayEmitsPressed()
    {
        var viewport = new SubViewport { Size = new Vector2I(400, 300) };
        var play = new Button { Position = new Vector2(20, 20), Size = new Vector2(120, 40) };
        try
        {
            Godot.Tree.Root.AddChild(viewport);
            viewport.AddChild(play);
            Godot.Engine.Iteration(); // Settle layout before aiming.
            using var pressed = GodotAssert.ExpectSignal(play, BaseButton.SignalName.Pressed);
            Godot.Input.Click(play);
            pressed.AssertEmitted();
        }
        finally { viewport.Free(); }
    }
}
```

The click is routed through Godot's GUI input pipeline. A foreground control
can block it, so the expectation checks whether the button actually received
the interaction. See [Simulating Input](/testing/input) for coordinate-based
clicks, keyboard focus, input suppression and global `Input` polling.

Inherited `SingleThreaded`, `NonParallelizable` and
`FixtureLifeCycle(LifeCycle.SingleInstance)` attributes keep setup, tests,
async continuations and teardown on the engine's owner thread. Preserve these
settings. Avoid `ConfigureAwait(false)`, `Task.Run` around Godot access, and
thread-switching attributes such as `Timeout` and `RequiresThread`.

Use derived `[OneTimeSetUp]`, `[SetUp]`, `[TearDown]` and `[OneTimeTearDown]`
methods for your own lifecycle work. They may return `Task`. Override
`CreateFixture()` to supply a custom `twodog.Testing.FixtureBase`.
Windowed macOS tests require a runner that owns the process main thread;
this host runs headless on Windows, Linux and macOS.

## Async Work and Signals

A raw `await Task.Delay(...)` preserves the engine thread but does not advance
Godot. Use the bounded frame helpers for operations that need its main loop:

```csharp
await Godot.WaitUntilAsync(() => node.IsNodeReady());
await Godot.AwaitAsync(SomeOperationThatNeedsFrames());

using var timeout = GodotAssert.ExpectSignal(timer, global::Godot.Timer.SignalName.Timeout);
timer.Start();
await timeout.WaitAsync(Godot);
timeout.AssertEmitted();
```

Subscribe before triggering the behavior, including synchronous signals.
`AssertEmitted(count = 1)` checks an exact count. `WaitAsync(Godot, count: 2)`
pumps until at least two emissions arrive. `ExpectSignal<Node>(parent,
Node.SignalName.ChildEnteredTree)` records one argument per emission in
`Values`, in order. Dispose expectations to disconnect them.

Waits default to five seconds and observe NUnit cancellation. Override
`timeout` or `cancellationToken` when needed. Timeouts fail NUnit assertions;
a timed-out task wait cannot cancel the supplied task. Await one pumping
operation at a time and clean up any work you started.

## Native Cleanup and Errors

Free nodes in `finally`. Disposing their C# wrappers does not free native
nodes. `QueueFree()` schedules deletion; assert actual deletion with:

```csharp
node.QueueFree();
await GodotAssert.FreedAsync(Godot, node);
```

Unconsumed Godot errors and warnings fail tests, including deferred work,
startup and final fixture teardown. Consume expected reports with
`Godot.Errors.Expect("message fragment")`. Override `FailOnGodotErrors` to
opt out for a fixture; the log remains available for inspection.

## Running Tests

```bash
dotnet test MyGame.nunit -c Debug
dotnet test MyGame.nunit -c Release
dotnet test MyGame.nunit -c Editor
dotnet test MyGame.nunit --filter "FullyQualifiedName~MenuTests"
```

Editor configuration adds the matching Editor binding reference and `EDITOR`
define. Resource import runs automatically during the build. The scaffold
includes eight runnable examples of async work, signals and deferred deletion.
