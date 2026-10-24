---
title: Writing Engine Tests
description: "Wait for Godot frames, assert signals, and clean up test state in xUnit and NUnit."
tagline: "Learn how to wait for frames, catch signals and keep tests tidy."
keywords: "Godot C# unit testing, Godot signals"
---

# Writing engine tests

A test that uses Godot often needs more than a method call: a timer needs
frames, a signal needs a listener, and a queued deletion needs time to finish.
The testing helpers handle these steps on the engine's thread.

The examples use `godot` for the shared fixture:

| Framework | Fixture                                        | Helpers namespace      |
|-----------|------------------------------------------------|------------------------|
| xUnit     | Constructor argument `godot`                   | `twodog.Testing.Xunit` |
| NUnit     | Use `var godot = EngineFixture;` inside a test | `twodog.Testing.NUnit` |

## Wait for frames

An ordinary `await Task.Delay(...)` does not advance Godot. Choose a helper
when the work needs the main loop:

| Helper                                  | Waits for                                                      |
|-----------------------------------------|----------------------------------------------------------------|
| `godot.WaitUntilAsync(() => condition)` | A condition to become true                                     |
| `godot.AwaitAsync(task)`                | A task to finish; returns its result or rethrows its exception |
| `signal.WaitAsync(godot)`               | A recorded signal emission                                     |
| `GodotAssert.FreedAsync(godot, node)`   | A native object to be deleted                                  |

For example, wrap an operation that awaits a Godot signal:

```csharp
async Task NextFrame()
{
    await godot.Tree.ToSignal(godot.Tree, SceneTree.SignalName.ProcessFrame);
}

await godot.AwaitAsync(NextFrame());
await godot.WaitUntilAsync(() => node.IsNodeReady(),
    timeout: TimeSpan.FromSeconds(2));
```

::: info Timeouts and cancellation
By default, a wait times out after five seconds. A wait also stops when your
test framework cancels the test. To set a different timeout, pass `timeout:`.
To add your own cancellation token, pass `cancellationToken:`. A timeout fails
the test, but it cannot cancel a task that you supplied. Cancel or clean up
that work yourself.
:::

Await only one wait helper at a time. Keep Godot access on the engine's owner
thread, also after `await`. Avoid `Task.Run` and `ConfigureAwait(false)`.

## Check a signal

Create the signal expectation before you trigger the behavior, so that it also
catches synchronous signals. If the signal can arrive later, wait for it. Then
assert the count:

```csharp
using var timeout = GodotAssert.ExpectSignal(timer, global::Godot.Timer.SignalName.Timeout);
timer.Start();
await timeout.WaitAsync(godot);
timeout.AssertEmitted();
```

| Call                              | Meaning                                           |
|-----------------------------------|---------------------------------------------------|
| `AssertEmitted()`                 | Exactly one emission                              |
| `AssertEmitted(0)`                | No emissions                                      |
| `WaitAsync(godot, count: 2)`      | Wait until at least two emissions arrive          |
| `ExpectSignal<T>(source, signal)` | Record a single argument per emission in `Values` |

```csharp
using var entered = GodotAssert.ExpectSignal<Node>(parent, Node.SignalName.ChildEnteredTree);
parent.AddChild(child);
entered.AssertEmitted();
var receivedChild = entered.Values[0];
```

Expectations disconnect on disposal, timeout, or cancellation. Disposal does
not check the emission count; call `AssertEmitted()` for that. A wait fails if
the source is freed before the expected signal arrives.
For signals with several arguments, connect a typed callable and use
`WaitUntilAsync` on the values you record.

## Clean up native objects

Free nodes in a `finally` block, so that cleanup also occurs when an assertion
fails. If you dispose a node's C# wrapper, Godot does not free the native node.
Also dispose the resources and signal expectations that your test owns.

`QueueFree()` schedules deletion. To check that it actually happened:

```csharp
node.QueueFree();
await GodotAssert.FreedAsync(godot, node);
```

::: tip Tree exit and deletion are separate
Godot emits `TreeExiting` while a node is still in the tree, and `TreeExited`
after the node leaves the tree. If you remove a node from its parent, Godot does
not free it.
:::

## Expect Godot errors

If an engine test does not consume a Godot error or warning, the test fails.
This also applies to exceptions that Godot catches in C# callbacks. If a test
triggers an error on purpose, consume the error and check its text. `Expect`
consumes every pending report, and each report must contain the text:

```csharp
player.Health = -1;
godot.Errors.Expect("Health must not be negative");
```

Errors from startup or deferred work between tests fail the next test on that
fixture. Both integrations also check remaining reports after engine shutdown.
xUnit can report late cleanup errors as collection failures. Tests without a
2dog fixture are not checked.

::: details Opting out of automatic error checks
If possible, expect the known error instead of opting out. Then unrelated
errors still fail the test.

| Framework | Opt out                                                                                                                          |
|-----------|----------------------------------------------------------------------------------------------------------------------------------|
| xUnit     | Mark a test or class `[AllowGodotErrors]`, or set `<TwoDogFailOnGodotErrors>false</TwoDogFailOnGodotErrors>` in the test project |
| NUnit     | Mark a test or class `[AllowGodotErrors]`, or override `FailOnGodotErrors` to return `false` for the whole fixture               |

`AllowGodotErrors` clears reports after the allowed test. If you disable
automatic checks for the whole project or fixture, the reports stay in the log,
so you can inspect them.
:::

## Run locally or in CI

```bash
dotnet test MyGame.xunit
dotnet test MyGame.nunit -c Release
dotnet test MyGame.xunit --filter-class "*SceneTests"
dotnet test MyGame.nunit --output Detailed
```

`dotnet test` with no project runs every test host; see
[Test runners](/testing/#test-runners). For filters and output options, see
[xUnit's](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
and [NUnit's](https://docs.nunit.org/articles/vs-test-adapter/NUnit-And-Microsoft-Test-Platform.html)
MTP documentation.

Resource import runs automatically when the test project builds. To use Editor
APIs, add `-c Editor`. Then check your
[xUnit](/configuration/xunit) or [NUnit](/configuration/nunit) settings.

Use a headless fixture in CI; it needs no display or audio device:

```yaml
- name: Run tests
  run: dotnet test MyGame.xunit --configuration Release
```
