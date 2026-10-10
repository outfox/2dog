---
title: Writing Engine Tests
description: "Wait for Godot frames, assert signals, and clean up test state in xUnit and NUnit."
---

# Writing engine tests

A test that uses Godot often needs more than a method call: a timer needs
frames, a signal needs a listener, and a queued deletion needs time to finish.
The testing helpers handle these steps on the engine's thread.

The examples use `godot` for the shared fixture:

| Framework | Fixture | Helpers namespace |
| --- | --- | --- |
| xUnit | Constructor argument `godot` | `twodog.Testing.Xunit` |
| NUnit | Use `var godot = Godot;` inside a test | `twodog.Testing.NUnit` |

## Wait for frames

An ordinary `await Task.Delay(...)` does not advance Godot. Choose a helper
when the work needs the main loop:

| Helper | Waits for |
| --- | --- |
| `godot.WaitUntilAsync(() => condition)` | A condition to become true |
| `godot.AwaitAsync(task)` | A task to finish; returns its result or propagates its failure |
| `signal.WaitAsync(godot)` | A recorded signal emission |
| `GodotAssert.FreedAsync(godot, node)` | A native object to be deleted |

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
Waits default to five seconds and observe your test framework's cancellation
token. You can pass `timeout:` and `cancellationToken:` to override or extend
those defaults. A timeout fails the test but cannot cancel a task you supplied;
cancel or clean up that work yourself.
:::

Await one pumping operation at a time. Keep Godot access on the fixture's
thread, including after `await`; avoid `Task.Run` and `ConfigureAwait(false)`.

## Check a signal

Subscribe before you trigger the behavior, so synchronous signals are caught
too. Then wait if needed and assert the count:

```csharp
using var timeout = GodotAssert.ExpectSignal(timer, global::Godot.Timer.SignalName.Timeout);
timer.Start();
await timeout.WaitAsync(godot);
timeout.AssertEmitted();
```

| Call | Meaning |
| --- | --- |
| `AssertEmitted()` | Exactly one emission |
| `AssertEmitted(0)` | No emissions |
| `WaitAsync(godot, count: 2)` | Pump until at least two emissions arrive |
| `ExpectSignal<T>(source, signal)` | Record a single argument per emission in `Values` |

```csharp
using var entered = GodotAssert.ExpectSignal<Node>(parent, Node.SignalName.ChildEnteredTree);
parent.AddChild(child);
entered.AssertEmitted();
var receivedChild = entered.Values[0];
```

Expectations disconnect on disposal, timeout, or cancellation. Disposal does
not assert. A wait fails if the source is freed before the expected signal.
For signals with several arguments, connect a typed callable and use
`WaitUntilAsync` on the values you record.

## Clean up native objects

Free nodes in `finally` so cleanup still happens when an assertion fails.
Disposing a node's C# wrapper does not free the native node. Dispose resources
and signal expectations that your test owns too.

`QueueFree()` schedules deletion. To check that it actually happened:

```csharp
node.QueueFree();
await GodotAssert.FreedAsync(godot, node);
```

::: tip Tree exit and deletion are separate
`TreeExiting` fires while a node is still in the tree; `TreeExited` fires after
removal. Removing a node from its parent does not free it.
:::

## Expect Godot errors

Unconsumed Godot errors and warnings fail engine tests, including exceptions
Godot catches in C# callbacks. If a test intentionally triggers an error,
consume it and check its text:

```csharp
player.Health = -1;
godot.Errors.Expect("Health must not be negative");
```

Errors from startup or deferred work between tests fail the next test on that
fixture. NUnit also checks after user fixture teardown, before shutting down
the engine. Tests without a 2dog fixture are not checked.

::: details Opting out of automatic error checks
Prefer expecting a known error so unrelated failures still surface.

| Framework | Opt out |
| --- | --- |
| xUnit | Mark a test or class `[AllowGodotErrors]`, or set `<TwoDogFailOnGodotErrors>false</TwoDogFailOnGodotErrors>` in the test project |
| NUnit | Override `FailOnGodotErrors` to return `false` in your fixture |

The fixture's error log remains available for inspection.
:::

## Run locally or in CI

```bash
dotnet test MyGame.xunit
dotnet test MyGame.nunit -c Release
dotnet test MyGame.xunit --filter "FullyQualifiedName~SceneTests"
dotnet test MyGame.nunit --logger "console;verbosity=detailed"
```

Resource import runs automatically when the test project builds. For Editor
APIs, choose `-c Editor` and check your
[xUnit](/configuration/xunit) or [NUnit](/configuration/nunit) settings.

Use a headless fixture in CI, with a dummy audio driver:

```yaml
- name: Run tests
  run: dotnet test MyGame.xunit --configuration Release
  env:
    GODOT_AUDIO_DRIVER: Dummy
```
