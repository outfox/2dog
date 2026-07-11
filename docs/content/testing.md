# Testing with xUnit

The test fixtures (`GodotFixture`, `GodotHeadlessFixture`, `GodotFixtureBase`) ship in the **`2dog`**
package, in the `twodog.fixture` namespace. The **`2dog.xunit`** package adds ready-made xUnit
collection definitions on top of them.

## Installation

```bash
dotnet add package 2dog.xunit          # pulls in 2dog (the fixtures) automatically
dotnet add package xunit.v3
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit.runner.visualstudio
```

## Fixtures

Both fixtures are thin subclasses of `GodotFixtureBase`, which starts the engine
in its constructor and exposes the members you use in tests:

```csharp
public abstract class GodotFixtureBase : IDisposable
{
    protected GodotFixtureBase(params string[] cmdLineArgs);

    public Engine Engine { get; }
    public GodotInstance GodotInstance { get; }
    public SceneTree Tree { get; }
}
```

### GodotFixture

Starts Godot with rendering enabled. Use for tests that need visual output.

```csharp
public class GodotFixture : GodotFixtureBase;
```

### GodotHeadlessFixture

Starts Godot in headless mode (`--headless`). Use for CI/CD and tests that don't need rendering.

```csharp
public class GodotHeadlessFixture() : GodotFixtureBase("--headless");
```

## Collections

Because Godot allows only one instance at a time, Godot tests must run through
xUnit collections with `DisableParallelization = true`. Tests within a
collection share that collection's fixture (one engine instance).

`2dog.xunit` ships ready-made collections for you  –  `GodotCollection` (full rendering) and
`GodotHeadlessCollection` (headless, recommended for CI). They are compiled directly into your test
assembly  –  xUnit only discovers `[CollectionDefinition]` classes that live in the test assembly
itself, so a definition shipped as a plain referenced DLL would be silently ignored. You therefore
just reference the package and use them:

```csharp
using twodog.xunit;

[Collection<GodotHeadlessCollection>]
public class MyTests(GodotHeadlessFixture godot) { /* ... */ }
```

### Multiple collections

Since the engine can be restarted in the same process (Godot 4.7), you can
also define **several** Godot collections. xUnit runs them sequentially and
disposes one collection's fixture before creating the next, so each
collection gets its own fresh engine instance:

```csharp
[CollectionDefinition(nameof(MyGodotCollectionA), DisableParallelization = true)]
public class MyGodotCollectionA : ICollectionFixture<GodotHeadlessFixture>;

[CollectionDefinition(nameof(MyGodotCollectionB), DisableParallelization = true)]
public class MyGodotCollectionB : ICollectionFixture<GodotHeadlessFixture>;

[Collection(nameof(MyGodotCollectionA))]
public class TestsAgainstEngineA(GodotHeadlessFixture godot) { /* ... */ }

[Collection(nameof(MyGodotCollectionB))]
public class TestsAgainstEngineB(GodotHeadlessFixture godot) { /* ... */ }
```

::: warning
`DisableParallelization = true` is required (it is already set on the shipped collections). Godot is
not thread-safe, and parallel tests will crash.
:::

### Custom collections

Need different Godot arguments? Subclass `GodotFixtureBase` and write a one-line collection in your
own test project:

```csharp
using twodog.fixture;
using Xunit;

public class GodotOpenGl3Fixture() : GodotFixtureBase("--display-driver", "opengl3");

[CollectionDefinition(nameof(GodotOpenGl3Collection), DisableParallelization = true)]
public class GodotOpenGl3Collection : ICollectionFixture<GodotOpenGl3Fixture>;
```

## Writing Tests

::: warning Godot Types in MemberData
Using Godot types like `NodePath` or `StringName` in `[MemberData]` will crash the test runner during discovery. Use `DisableDiscoveryEnumeration = true` or pass primitive types instead. See [Known Issues](/known-issues/xunit-discovery) for details.
:::

```csharp
[Collection<GodotHeadlessCollection>]
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
# Run all tests (default: Debug configuration)
dotnet test

# Run with specific configuration
dotnet test -c Debug      # template_debug build
dotnet test -c Release    # template_release build
dotnet test -c Editor     # editor build with TOOLS_ENABLED

# Run with output
dotnet test --logger "console;verbosity=detailed"

# Run specific test
dotnet test --filter "FullyQualifiedName~SceneTests"
```

### Test Configurations

Different build configurations are useful for different test scenarios:

| Configuration | Use Case |
|--------------|----------|
| **Debug** | General unit tests, debugging |
| **Release** | Performance tests, final validation |
| **Editor** | Tests that need import pipeline or editor APIs |

Example: Testing asset import functionality:
```csharp
[Collection<GodotHeadlessCollection>]
public class ImportTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void ImportTexture_ValidFile_Succeeds()
    {
        // This test requires Editor configuration
        // Build with: dotnet test -c Editor
        var importer = ResourceImporterTexture.Singleton;
        Assert.NotNull(importer);
        
        // Test import pipeline functionality
    }
}
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

For test projects using `ProjectReference` to twodog (not the NuGet package), configure build types:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\twodog\twodog.csproj" />
  </ItemGroup>
  <!-- Fixtures come from the twodog project above. Note: with a ProjectReference the 2dog.xunit
       compile-in collections are NOT imported automatically  –  reference the 2dog.xunit NuGet
       package instead, or define a collection locally (see "Custom collections"). -->

  <!-- Configuration-specific build types -->
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <TwoDogBuildType>template_debug</TwoDogBuildType>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <TwoDogBuildType>template_release</TwoDogBuildType>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Editor'">
    <TwoDogBuildType>editor</TwoDogBuildType>
  </PropertyGroup>

  <!-- Required for ProjectReference builds -->
  <Import Project="..\twodog\build\2dog.targets" />
</Project>
```

### Platform-Specific Native Libraries

When building from source, test projects need the appropriate platform variant:

```xml
<!-- Debug configuration: use debug variant -->
<ItemGroup Condition="$([MSBuild]::IsOSPlatform('Windows')) And '$(Configuration)' == 'Debug'">
  <ProjectReference Include="..\platforms\twodog.win-x64\twodog.win-x64.debug.csproj"/>
</ItemGroup>

<!-- Release configuration: use release variant -->
<ItemGroup Condition="$([MSBuild]::IsOSPlatform('Windows')) And '$(Configuration)' == 'Release'">
  <ProjectReference Include="..\platforms\twodog.win-x64\twodog.win-x64.release.csproj"/>
</ItemGroup>

<!-- Editor configuration: use editor variant -->
<ItemGroup Condition="$([MSBuild]::IsOSPlatform('Windows')) And '$(Configuration)' == 'Editor'">
  <ProjectReference Include="..\platforms\twodog.win-x64\twodog.win-x64.editor.csproj"/>
</ItemGroup>
```
