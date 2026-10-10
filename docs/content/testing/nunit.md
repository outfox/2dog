---
title: Testing with NUnit
description: "Write your first Godot test with NUnit's shared headless fixture."
---

# Testing with NUnit

Add a test project from your Godot project directory:

```bash
dnx 2dog add --nunit
dotnet test MyGame.nunit
```

The generated `BasicTests.cs` has runnable examples of async work, signals,
and deletion. Extend them with tests for your game.

## Your first test

Derive from `GodotTestFixture`. Its `Godot` property gives your test class a
shared headless engine. This test waits for a real Godot timer:

```csharp
using Godot;
using NUnit.Framework;
using twodog.Testing.NUnit;

public class TimerTests : GodotTestFixture
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

`Godot` exposes `Engine`, `GodotInstance`, `Tree`, `Input`, and `Errors`.
Each derived test class gets one engine; clean up the objects each test creates.

## Setup and teardown

Use your own `[SetUp]` and `[TearDown]` methods for per-test work, or
`[OneTimeSetUp]` and `[OneTimeTearDown]` for work shared by the class.
These methods may return `Task`.

::: warning Keep the engine on its thread
Preserve the inherited `SingleThreaded`, `NonParallelizable`, and
`FixtureLifeCycle(LifeCycle.SingleInstance)` settings. Avoid `Task.Run`,
`ConfigureAwait(false)`, and thread-switching NUnit attributes such as
`Timeout` and `RequiresThread` around Godot access.
:::

## Rendering tests

`CreateFixture()` can return a rendering `twodog.Testing.Fixture` instead of
`HeadlessFixture`. Inside a derived test fixture, with `using twodog.Testing`:

```csharp
protected override FixtureBase CreateFixture() => new Fixture();
```

Each NUnit fixture owns its engine; this integration does not share an engine
across test classes like an xUnit collection. Rendering also has runner
requirements:

| Platform | Requirement |
| --- | --- |
| Linux | A display and a compatible renderer |
| Windows | STA for the whole fixture, plus common-controls v6 activation in the test host |
| macOS | Engine startup, tests, and teardown on the process main thread |

::: warning Rendering is not configured by the NUnit scaffold
The standard host runs headless on all three platforms. An STA fixture alone
does not supply Windows common-controls activation, and the host does not
reserve macOS's main thread. Changing `CreateFixture()` alone is not enough
on these platforms. Use xUnit's rendering collection for the supported setup.
:::

## Parameterized fixtures and test cases

Use NUnit's [parameterized fixtures](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testfixture.html#parameterized-test-fixtures)
to run a test class with several configurations. Each fixture instance gets
its own engine. Keep constructor data managed; create Godot objects after
the engine starts.

For method-level inputs, `[TestCase]` supplies rows, `[Combinatorial]` combines
parameter values, and `[Pairwise]` covers every pair with fewer cases. See
[NUnit's data attributes](https://docs.nunit.org/articles/nunit/writing-tests/attributes/pairwise.html).

## Waits, signals, and input

Continue with [Writing engine tests](./writing-tests) for frame waits, signal
arguments, cleanup, and expected errors. Use [Simulating input](./input) for
UI tests. Both guides work with NUnit and xUnit.

See [NUnit configuration](/configuration/nunit) for packages and Editor tests.
