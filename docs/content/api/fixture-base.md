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
| `GodotInstance` | `Godot.GodotInstance` | Running native instance |
| `Tree` | `Godot.SceneTree` | Active scene tree |

## `Dispose`

```csharp
public void Dispose()
```

Disposes `GodotInstance`, then `Engine`. Let the test framework call this
through its fixture lifetime.

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
