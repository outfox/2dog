---
title: Fixtures
description: "API reference for 2dog's test fixtures and xUnit collections: FixtureBase, HeadlessFixture, Fixture, HeadlessCollection and RenderingCollection."
tagline: "Learn how test fixtures own a Godot instance for your tests."
---

# Fixtures

Each fixture starts and owns one Godot engine for the tests that use it.

| Type                                                        | Use                                                                                        |
|-------------------------------------------------------------|--------------------------------------------------------------------------------------------|
| [`HeadlessFixture`](#headlessfixture)                       | Starts Godot with `--headless`; the usual fixture for game logic, scenes, resources and CI |
| [`Fixture`](#fixture)                                       | Starts Godot with rendering, for tests that need a display, rendered frames or windows     |
| [`FixtureBase`](#fixturebase)                               | Base class; derive from it to pass other Godot arguments                                   |
| [`HeadlessCollection`, `RenderingCollection`](#collections) | Non-parallel xUnit collections that share one fixture                                      |

## `FixtureBase` {#fixturebase}

```csharp
public abstract class FixtureBase : IDisposable
```

**Package:** `2dog.engine`  
**Namespace:** `twodog.Testing`

The constructor resolves the Godot project directory, preloads game assemblies,
and starts the engine without its configured main scene.

### Constructor

```csharp
protected FixtureBase(params string[] cmdLineArgs)
```

Passes each argument to Godot unchanged. One process supports either headless
or rendering fixtures, not both. The constructor rejects a change from one mode
to the other.

### Properties

| Property        | Type                       | Description                                                       |
|-----------------|----------------------------|-------------------------------------------------------------------|
| `Engine`        | `twodog.Engine`            | Engine owned by the fixture                                       |
| `GodotInstance` | `Godot.GodotInstance`      | Borrowed compatibility handle                                     |
| `Tree`          | `Godot.SceneTree`          | Active scene tree                                                 |
| `Errors`        | `twodog.GodotErrorLog`     | Errors and warnings Godot reported and no test consumed yet       |
| `Input`         | `twodog.Testing.TestInput` | Input server events, viewport routing and control-position clicks |

### `Input`

Use `Input.Click(control)` to aim at a control through its viewport's input
router. Foreground controls and viewport input
suppression keep their normal behavior. `PushToViewport` routes custom events
locally. `Send` uses the input server and also updates the global polling
state. Call these on the engine's owner thread. See
[Simulating Input](/testing/input) for examples and the distinction between modes.

### `Errors`

The fixture starts its engine with `CaptureErrors` enabled. Every error and warning that Godot reports goes to
`Errors`: `push_error`, failed engine checks, and exceptions that Godot catches in C# callbacks. With
`2dog.xunit` or `2dog.nunit`, a test fails if any errors or warnings remain in `Errors` after it. A test that
expects an error consumes it and asserts its text:

```csharp
godot.Errors.Expect("Health must not be negative");
```

`Drain()` returns and clears the log without asserting anything.

### `Dispose`

```csharp
public void Dispose()
```

Disposes the `Engine` that the fixture owns. Let the test framework call this
at the end of the fixture lifetime.

## `HeadlessFixture` {#headlessfixture}

```csharp
public class HeadlessFixture : FixtureBase
```

Starts Godot with `--headless`. Use it for game logic, scenes, resources and CI.

## `Fixture` {#fixture}

```csharp
public class Fixture : FixtureBase
```

Starts Godot with rendering enabled, for tests that need a display, rendered
frames or window behavior.

## xUnit Collections {#collections}

```csharp
public class HeadlessCollection : ICollectionFixture<HeadlessFixture>
public class RenderingCollection : ICollectionFixture<Fixture>
```

**Package:** `2dog.xunit`  
**Namespace:** `twodog.Testing.Xunit`

Each collection is non-parallel and shares one fixture across its test classes.
The package compiles the definitions into your test assembly so xUnit can
discover them. Remove the nodes and other state that each test adds, so that
they do not affect the next test.

```csharp
using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit;

[Collection<HeadlessCollection>]
public class SceneTests(HeadlessFixture godot)
{
    [Fact]
    public void SceneTree_IsRunning() => Assert.NotNull(godot.Tree.Root);
}
```

### Custom collection

To pass other Godot arguments, derive from `FixtureBase`. Define a non-parallel
collection for the new fixture, because Godot is not thread-safe.

```csharp
using twodog.Testing;
using Xunit;

public class OpenGl3Fixture()
    : FixtureBase("--rendering-driver", "opengl3");

[CollectionDefinition(nameof(OpenGl3Collection), DisableParallelization = true)]
public class OpenGl3Collection : ICollectionFixture<OpenGl3Fixture>;
```
