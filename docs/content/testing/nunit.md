---
title: Testing with NUnit
description: "Write Godot tests with NUnit's headless or rendering fixture."
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

Derive from `GodotTestFixture`. Its `EngineFixture` property gives your test
class a shared headless engine. This test waits for a real Godot timer:

```csharp
using Godot;
using Timer = Godot.Timer;
using NUnit.Framework;
using twodog.Testing.NUnit;

public class TimerTests : GodotTestFixture
{
    [Test]
    public async Task TimerEmitsTimeout()
    {
        var timer = new Timer { OneShot = true, WaitTime = 0.01 };
        try
        {
            EngineFixture.Tree.Root.AddChild(timer);
            using var timeout = GodotAssert.ExpectSignal(
                timer, Timer.SignalName.Timeout);
            timer.Start();
            await timeout.WaitAsync(EngineFixture);
            timeout.AssertEmitted();
        }
        finally { timer.Free(); }
    }
}
```

`EngineFixture` exposes `Engine`, `GodotInstance`, `Tree`, `Input`, and `Errors`.
Each derived test class gets one engine; clean up the objects each test creates.

::: details Why the Timer alias?
`using Timer = Godot.Timer;` distinguishes Godot's timer from .NET's timer.
The older fixture property named `Godot` remains a compatibility alias for
`EngineFixture`; inside a derived class it also shadows the `Godot` namespace.
The type alias avoids needing `global::Godot.Timer` in the test body.
:::

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

Derive from `GodotRenderingTestFixture` to open a window and render frames:

```csharp
public class RenderingTests : GodotRenderingTestFixture
{
    [Test]
    public void WindowIsVisible()
        => Assert.That(EngineFixture.Tree.Root.Visible, Is.True);
}
```

Each NUnit fixture owns its engine; this integration does not share an engine
across test classes like an xUnit collection.

| Platform | Requirement |
| --- | --- |
| Linux | A display and a compatible renderer |
| Windows | A display; the fixture supplies STA and common-controls activation |
| macOS | Use the headless fixture; the standard NUnit runner does not reserve the process main thread |

::: warning Use a separate rendering test project
The scaffold uses headless fixtures. Keep rendering fixtures in another test
project so each mode runs in its own process. Switching display modes during
native Godot restarts can crash; 2dog's fixtures reject the switch.
For windowed macOS tests, use xUnit's rendering collection for now.
:::

::: details Custom engine arguments
Override `CreateFixture()` in either base class. Define a fixture with the
arguments you need, such as a compatibility renderer:

```csharp
public class OpenGlFixture()
    : twodog.Testing.FixtureBase("--rendering-driver", "opengl3");

public class UiTests : GodotRenderingTestFixture
{
    protected override twodog.Testing.FixtureBase CreateFixture()
        => new OpenGlFixture();
}
```

Use `GodotRenderingTestFixture` only on Windows/Linux. An override does not
remove macOS's requirement for the process main thread.
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
