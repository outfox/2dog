---
title: Testing with NUnit
description: "Write Godot tests with NUnit's headless or rendering fixture."
tagline: "Learn how to write your first Godot tests with NUnit."
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

Fixtures skip the configured main scene, but project autoloads still
initialize. Load and instantiate scenes explicitly in your tests. If a scene
changes shared state such as `SceneTree.Paused`, restore that state.

::: details Why the Timer alias?
The fixture's `Godot` compatibility property shadows the `Godot` namespace in
derived classes, so alias Godot types such as `Timer`.
:::

## Setup and teardown

Use your own `[SetUp]` and `[TearDown]` methods for per-test work, or
`[OneTimeSetUp]` and `[OneTimeTearDown]` for work shared by the class.
These methods can return `Task`.

::: warning Keep the engine on its thread
Do not change the inherited `SingleThreaded`, `NonParallelizable`, and
`FixtureLifeCycle(LifeCycle.SingleInstance)` settings. Around Godot access, do
not use `Task.Run`, `ConfigureAwait(false)`, or NUnit attributes that switch
threads, such as `Timeout` and `RequiresThread`. Godot access must stay on the
engine's owner thread.
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

Each NUnit fixture owns its engine. Unlike an xUnit collection, this
integration does not share an engine across test classes.

| Platform | Requirement                                                                                  |
|----------|----------------------------------------------------------------------------------------------|
| Linux    | A display and a compatible renderer                                                          |
| Windows  | A display; the fixture supplies STA and common-controls activation                           |
| macOS    | Use the headless fixture; the standard NUnit runner does not reserve the process main thread |

::: warning Use a separate rendering test project
Keep rendering fixtures in a separate test project, so that each mode runs in
its own process. The setup uses headless fixtures. Native Godot can crash if
one process switches display modes between engine restarts, so 2dog's fixtures
reject the switch. For windowed macOS tests, use xUnit's rendering collection
for now.
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

Use `GodotRenderingTestFixture` only on Windows or Linux. On macOS, windowed
Godot needs the process main thread, and an override does not change that.
:::

## Parameterized fixtures and test cases

Use NUnit's [parameterized fixtures](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testfixture.html#parameterized-test-fixtures)
to run a test class with several configurations. Each fixture instance gets
its own engine. Pass only plain .NET values, not Godot
types, as constructor arguments. Create Godot objects after the engine starts.

For method-level inputs, `[TestCase]` supplies rows, `[Combinatorial]` combines
parameter values, and `[Pairwise]` covers every pair with fewer cases. See
[NUnit's data attributes](https://docs.nunit.org/articles/nunit/writing-tests/attributes/pairwise.html).

## Waits, signals, and input

Use [Writing engine tests](./writing-tests) for frame waits, signal
expectations, cleanup, and expected errors. Use [Simulating input](./input) for
UI tests. Both guides work with NUnit and xUnit.

See [NUnit configuration](/configuration/nunit) for packages and Editor tests.
