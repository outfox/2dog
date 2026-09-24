---
title: twodog.Testing.FixtureBase
description: "API reference for FixtureBase, the abstract base class for test fixtures that own a Godot instance - constructor, properties, disposal, and custom xUnit collections."
---

# `twodog.Testing.FixtureBase`

Base class for test fixtures that own a Godot instance.

```csharp
public abstract class FixtureBase : IDisposable
```

**Package:** `2dog.engine`  
**Namespace:** `twodog.Testing`

The constructor resolves the Godot project directory, preloads game assemblies,
and starts the engine. Derive from this class when the ready-made fixtures do
not pass the arguments your tests need.

## Constructor

```csharp
protected FixtureBase(params string[] cmdLineArgs)
```

Passes each argument to Godot unchanged.

```csharp
public class OpenGl3Fixture()
    : FixtureBase("--rendering-driver", "opengl3");
```

## Properties

| Property | Type | Description |
| --- | --- | --- |
| `Engine` | `twodog.Engine` | Engine owned by the fixture |
| `GodotInstance` | `Godot.GodotInstance` | Borrowed compatibility handle |
| `Tree` | `Godot.SceneTree` | Active scene tree |
| `Errors` | `twodog.GodotErrorLog` | Errors and warnings Godot reported and no test consumed yet |

## `Errors`

The fixture starts its engine with `CaptureErrors` enabled, so every error and warning Godot reports lands in
`Errors`: `push_error`, failed engine checks, and exceptions Godot catches in C# callbacks. With `2dog.xunit`,
a test fails when it leaves any behind. A test that expects one consumes it, asserting its text:

```csharp
godot.Errors.Expect("Health must not be negative");
```

`Drain()` returns and clears the log without asserting anything.

## `Dispose`

```csharp
public void Dispose()
```

Disposes the owning `Engine`. Let the test framework call this through its
fixture lifetime.

## Custom xUnit Collection

```csharp
using twodog.Testing;
using Xunit;

public class OpenGl3Fixture()
    : FixtureBase("--rendering-driver", "opengl3");

[CollectionDefinition(nameof(OpenGl3Collection), DisableParallelization = true)]
public class OpenGl3Collection : ICollectionFixture<OpenGl3Fixture>;
```

Godot is not thread-safe. Keep custom engine collections non-parallel.
