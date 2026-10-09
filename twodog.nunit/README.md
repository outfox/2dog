# 2dog.nunit

Run [NUnit](https://nunit.org/) tests against a real headless Godot engine.
Reference `2dog.nunit`, `NUnit3TestAdapter` and `Microsoft.NET.Test.Sdk`, or scaffold:

```text
dnx 2dog add --nunit
dotnet test MyGame.nunit
```

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
            using var signal = GodotAssert.ExpectSignal(timer, global::Godot.Timer.SignalName.Timeout);
            timer.Start();
            await signal.WaitAsync(Godot);
            signal.AssertEmitted();
        }
        finally { timer.Free(); }
    }
}
```

`GodotTestFixture` shares an engine across the tests in each derived fixture.
NUnit's inherited `SingleThreaded`, `NonParallelizable` and
`FixtureLifeCycle(LifeCycle.SingleInstance)`
attributes keep setup, tests, async continuations and teardown on the engine's
owner thread. Do not use `ConfigureAwait(false)` before accessing Godot, or
thread-switching attributes such as `Timeout` or `RequiresThread` on these tests.
The host runs headless on Windows, Linux and macOS; windowed macOS tests need a
runner that owns the process main thread and are outside this host's scope.
Override `CreateFixture()` to supply a custom `twodog.Testing.FixtureBase`.

Godot errors and warnings fail tests, including startup and deferred errors.
Consume expected reports with `Godot.Errors.Expect("message fragment")`.
Override `FailOnGodotErrors` to opt out for a fixture.

The helpers in `twodog.Testing.NUnit` include:

- `GodotAssert.ExpectSignal(source, signal)` subscribes immediately, counts
  emissions and checks an exact count with `AssertEmitted(count = 1)`.
- `GodotAssert.ExpectSignal<T>(source, signal)` also records one argument per
  emission in `Values`, in emission order.
- `await signal.WaitAsync(Godot, count: 2)` pumps until at least two emissions.
- `await Godot.WaitUntilAsync(() => condition)` advances frames until true.
- `await Godot.AwaitAsync(task)` pumps frames and propagates the result or failure.
- `await GodotAssert.FreedAsync(Godot, node)` waits for actual native deletion
  after `QueueFree()`.

Waits default to five seconds, link NUnit's cancellation token, and permit one
active pump per fixture. Timeouts are NUnit assertion failures. Signal waits
disconnect on failure or cancellation; dispose expectations to disconnect after
success. Disposal does not assert. A timed-out wait does not cancel its supplied
task. Free native nodes in `finally`; disposing a C# wrapper does not free them.

The scaffold includes eight examples of async work, signals, typed arguments,
timers, tree lifecycle and deferred deletion. Use `dotnet test -c Editor` for
Editor APIs; the template adds the matching binding reference and `EDITOR`.
