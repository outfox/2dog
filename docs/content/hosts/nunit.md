---
title: NUnit
description: "Run NUnit tests against a real headless Godot engine with fixtures and signal expectations."
---

# NUnit

The NUnit host uses the [NUnit.org framework](https://nunit.org/) and a real
headless Godot engine. Add it to your Godot project:

```bash
dnx 2dog add --nunit
dotnet test MyGame.nunit
```

For a new project, use `dnx 2dog new MyGame --nunit`. The `dotnet new` template
supports `dotnet new 2dog -n MyGame --nunit true --tests false --web false`.
NUnit is opt-in; `--tests` continues to select xUnit.

## Fixture and Signals

Derive test classes from `GodotTestFixture` in `twodog.Testing.NUnit`.
The protected `Godot` property exposes the engine fixture, tree and error log.

```csharp
using Godot;
using NUnit.Framework;
using twodog.Testing.NUnit;

public class MyTests : GodotTestFixture
{
    [Test]
    public async Task TimerEmitsTimeout()
    {
        var timer = new global::Godot.Timer { OneShot = true, WaitTime = 0.01 };
        try
        {
            Godot.Tree.Root.AddChild(timer);
            using var timeout = GodotAssert.ExpectSignal(
                timer, global::Godot.Timer.SignalName.Timeout);
            timer.Start();
            await timeout.WaitAsync(Godot);
            timeout.AssertEmitted();
        }
        finally { timer.Free(); }
    }
}
```

Subscribe before triggering the behavior: synchronous signals are recorded too.
`AssertEmitted()` checks exactly one emission; pass a count to check another
number, including zero. `WaitAsync(Godot, count: 2)` advances frames until at
least two emissions arrive. `ExpectSignal<Node>(parent,
Node.SignalName.ChildEnteredTree)` records each single argument in `Values`.
Dispose expectations to disconnect; disposal itself does not assert.

## Async Work and Cleanup

Use `await Godot.AwaitAsync(task)` for async work that needs frames and
`await Godot.WaitUntilAsync(() => condition)` for a condition. Both advance
Godot's loop on its owner thread. A raw `ToSignal` await needs frames too:
wrap the task containing that await with `Godot.AwaitAsync`.

After `node.QueueFree()`, use `await GodotAssert.FreedAsync(Godot, node)` to
assert actual native deletion. Free native objects in `finally` when a test
may fail before cleanup; disposing their managed wrappers does not free them.

Waits default to five seconds and observe NUnit cancellation. Override the
`timeout` and `cancellationToken` arguments as needed. Timeouts fail NUnit
assertions. A failed signal wait disconnects its listener. Only one wait may
pump a fixture at a time; a timed-out task wait does not cancel that task.

## Thread Ownership and Errors

Each derived fixture shares one engine across its tests. Inherited NUnit
attributes keep fixtures sequential and keep setup, tests, async continuations
and teardown on the engine's owner thread. Avoid `ConfigureAwait(false)` before
Godot access and thread-switching NUnit attributes such as `Timeout` and
`RequiresThread`. This host supports headless execution on Windows, Linux and
macOS. Windowed macOS tests require a runner that owns the process main thread.

Unconsumed Godot errors and warnings fail tests. Consume expected errors with
`Godot.Errors.Expect("message fragment")`; startup and deferred errors fail
the next test. Override `FailOnGodotErrors` to opt out for a fixture, or
`CreateFixture()` to supply a custom `twodog.Testing.FixtureBase`.

The generated `BasicTests.cs` includes eight runnable examples. See
[Testing with NUnit](/testing/nunit) for the development guide and
[NUnit Configuration](/configuration/nunit) for package and variant settings.
