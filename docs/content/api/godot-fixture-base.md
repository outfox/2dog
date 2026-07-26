# `twodog.fixture.GodotFixtureBase`

Base class for xUnit fixtures that own a Godot instance.

```csharp
public abstract class GodotFixtureBase : IDisposable
```

**Package:** `2dog.engine`  
**Namespace:** `twodog.fixture`

The constructor resolves the Godot project directory, preloads game assemblies,
and starts the engine. Derive from this class when the ready-made fixtures do
not pass the arguments your tests need.

## Constructor

```csharp
protected GodotFixtureBase(params string[] cmdLineArgs)
```

Passes each argument to Godot unchanged.

```csharp
public class GodotOpenGl3Fixture()
    : GodotFixtureBase("--rendering-driver", "opengl3");
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

Disposes `GodotInstance`, then `Engine`. Let xUnit call this through its
collection-fixture lifetime.

## Custom Collection

```csharp
using twodog.fixture;
using Xunit;

public class GodotOpenGl3Fixture()
    : GodotFixtureBase("--rendering-driver", "opengl3");

[CollectionDefinition(nameof(GodotOpenGl3Collection), DisableParallelization = true)]
public class GodotOpenGl3Collection : ICollectionFixture<GodotOpenGl3Fixture>;
```

Godot is not thread-safe. Keep custom engine collections non-parallel.
