# Testing with xUnit

The `twodog.xunit` package provides test fixtures for writing xUnit tests against Godot.

## Installation

```bash
dotnet add package twodog.xunit
dotnet add package xunit
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit.runner.visualstudio
```

## Fixtures

### GodotFixture

Starts Godot with rendering enabled. Use for tests that need visual output.

```csharp
public class GodotFixture : IDisposable
{
    public Engine Engine { get; }
    public GodotInstance GodotInstance { get; }
    public SceneTree Tree { get; }
}
```

### GodotHeadlessFixture

Starts Godot in headless mode (`--headless`). Use for CI/CD and tests that don't need rendering.

```csharp
public class GodotHeadlessFixture : IDisposable
{
    public Engine Engine { get; }
    public GodotInstance GodotInstance { get; }
    public SceneTree Tree { get; }
}
```

## Collection Setup

Due to the single-instance limitation, all Godot tests must share one fixture. Use xUnit's collection fixtures:

```csharp
// Define the collection (once per test project)
[CollectionDefinition("Godot", DisableParallelization = true)]
public class GodotCollection : ICollectionFixture<GodotHeadlessFixture>
{
}
```

::: warning
`DisableParallelization = true` is required. Godot is not thread-safe, and parallel tests will crash.
:::

## Writing Tests

```csharp
[Collection("Godot")]
public class SceneTests
{
    private readonly GodotHeadlessFixture _godot;

    public SceneTests(GodotHeadlessFixture godot)
    {
        _godot = godot;
    }

    [Fact]
    public void LoadScene_ValidPath_Succeeds()
    {
        var scene = GD.Load<PackedScene>("res://test_scene.tscn");
        Assert.NotNull(scene);

        var instance = scene.Instantiate();
        _godot.Tree.Root.AddChild(instance);

        Assert.True(instance.IsInsideTree());

        instance.QueueFree();
    }

    [Fact]
    public void PhysicsServer_IsAvailable()
    {
        var physics = PhysicsServer3D.Singleton;
        Assert.NotNull(physics);
    }
}
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run with output
dotnet test --logger "console;verbosity=detailed"

# Run specific test
dotnet test --filter "FullyQualifiedName~SceneTests"
```

## CI/CD Configuration

Example GitHub Actions workflow:

```yaml
- name: Run Tests
  run: dotnet test --configuration Release
  env:
    # Use dummy drivers for headless CI
    GODOT_AUDIO_DRIVER: Dummy
```

## Project Configuration

For test projects using `ProjectReference` to twodog (not the NuGet package), import the build targets:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\twodog\twodog.csproj" />
    <ProjectReference Include="..\twodog.xunit\twodog.xunit.csproj" />
  </ItemGroup>

  <!-- Required for ProjectReference builds -->
  <Import Project="..\twodog\build\twodog.targets" />
</Project>
```
