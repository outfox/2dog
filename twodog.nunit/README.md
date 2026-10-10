# 2dog.nunit

Run [NUnit](https://nunit.org/) tests against a real Godot engine.
Reference `2dog.nunit`, `NUnit3TestAdapter` and `Microsoft.NET.Test.Sdk`, or scaffold:

```text
dnx 2dog add --nunit
dotnet test MyGame.nunit
```

```csharp
using Godot;
using Timer = Godot.Timer;
using NUnit.Framework;
using twodog.Testing.NUnit;

public class MyTests : GodotTestFixture
{
    [Test]
    public async Task TimerEmitsTimeout()
    {
        var timer = new Timer { OneShot = true, WaitTime = 0.01 };
        try
        {
            EngineFixture.Tree.Root.AddChild(timer);
            using var signal = GodotAssert.ExpectSignal(timer, Timer.SignalName.Timeout);
            timer.Start();
            await signal.WaitAsync(EngineFixture);
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
The default fixture runs headless on Windows, Linux and macOS. Derive from
`GodotRenderingTestFixture` for rendering on Windows/Linux with a display.
It supplies Windows STA; the engine fixture activates common-controls v6 even
inside NUnit's testhost. Windowed macOS tests still need a runner that owns the
process main thread; use xUnit's rendering collection there.
Override `CreateFixture()` to supply a custom `twodog.Testing.FixtureBase`.
Keep headless and rendering fixtures in separate test projects. Switching
display modes in one process can crash native Godot; the fixtures reject it.

Use `EngineFixture` to access the running fixture. The older `Godot` property
remains an alias for compatibility, but shadows the Godot namespace. Use
unqualified type names or aliases such as `using Timer = Godot.Timer;`.

Godot errors and warnings fail tests, including startup, deferred work and
engine shutdown.
Consume expected reports with `EngineFixture.Errors.Expect("message fragment")`.
Mark a test or class `[AllowGodotErrors]` to clear its reports after teardown.
Override `FailOnGodotErrors` to disable checking for a fixture and preserve
its error log.

The helpers in `twodog.Testing.NUnit` include:

- `GodotAssert.ExpectSignal(source, signal)` subscribes immediately, counts
  emissions and checks an exact count with `AssertEmitted(count = 1)`.
- `GodotAssert.ExpectSignal<T>(source, signal)` also records one argument per
  emission in `Values`, in emission order.
- `await signal.WaitAsync(EngineFixture, count: 2)` pumps until at least two emissions.
- `await EngineFixture.WaitUntilAsync(() => condition)` advances frames until true.
- `await EngineFixture.AwaitAsync(task)` pumps frames and propagates the result or failure.
- `await GodotAssert.FreedAsync(EngineFixture, node)` waits for actual native deletion
  after `QueueFree()`.

Waits default to five seconds, link NUnit's cancellation token, and permit one
active pump per fixture. Timeouts are NUnit assertion failures. Signal waits
disconnect on failure or cancellation; dispose expectations to disconnect after
success. Disposal does not assert. A timed-out wait does not cancel its supplied
task. Free native nodes in `finally`; disposing a C# wrapper does not free them.

The scaffold includes eight examples of async work, signals, typed arguments,
timers, tree lifecycle and deferred deletion. Use `dotnet test -c Editor` for
Editor APIs; the template adds the matching binding reference and `EDITOR`.
