# xUnit Host

`MyGame.xunit` starts a real Godot engine against your real project directory,
with your real resources loaded  –  `GD.Load<PackedScene>` loads the scene you
authored, `AddChild` puts it in a live `SceneTree`, `Iteration()` runs a real
physics frame. The only structural difference from the other hosts is that the
test runner owns the process, so the engine lifetime moves from a `Main()`
into an xUnit **fixture**.

```bash
dotnet test MyGame.xunit
```

::: info This page is the host project
[Testing with xUnit](/testing) covers fixtures, custom engine arguments, and
parallel collections. What follows is the host itself.
:::

## A Test

```csharp
using Godot;
using twodog.fixture;  // GodotHeadlessFixture
using twodog.xunit;    // GodotHeadlessCollection

namespace MyGame.Tests;

[Collection<GodotHeadlessCollection>]
public class BasicTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void LoadMainScene_Succeeds()
    {
        var mainScene = (string)ProjectSettings.GetSetting("application/run/main_scene", "");
        Assert.SkipWhen(mainScene == "", "No run/main_scene configured in project.godot");

        var instance = GD.Load<PackedScene>(mainScene).Instantiate();
        godot.Tree.Root.AddChild(instance);

        Assert.NotNull(instance.GetParent());
    }
}
```

The fixture is the host program in disguise  –  it starts the engine in its
constructor and exposes what a `Main()` would hold in local variables:

| Fixture member | Console-host equivalent |
| --- | --- |
| `godot.Engine` | `var engine = new Engine(...)` |
| `godot.GodotInstance` | `var godot = engine.Start()` |
| `godot.Tree` | `engine.Tree` |

`GodotHeadlessFixture` passes `--headless`; `GodotFixture` starts with
rendering. Both derive from `GodotFixtureBase`, which you can subclass with
any other Godot arguments.

## Collections Are Not Optional

Godot allows one instance at a time, so tests must run inside a collection
with `DisableParallelization = true`, and every test in a collection shares
that one engine. `2dog.xunit` ships `GodotCollection` and
`GodotHeadlessCollection` ready to use  –  as **compile-in source**, because
xUnit only discovers `[CollectionDefinition]` classes that live in the test
assembly itself. Referencing the package is all it takes.

::: warning Keep parallelization off
The generated `xunit.runner.json` sets `"parallelizeTestCollections": false`.
Several collections at once means several engines at once, which the
single-instance rule forbids. Sequentially, each collection gets a fresh
engine  –  the previous fixture is disposed before the next is created.
:::

Genuinely parallel engines are possible through the experimental
`twodog.hosting` stack  –  see
[Parallel collections](/testing#parallel-collections-one-engine-per-collection).

## The Project

The console host's csproj plus a test framework. The 2dog-specific parts are
identical: `2dog.xunit` (which brings `2dog.engine` transitively),
`GodotProjectDir`, the variant mapping, and a `.gdignore` beside the csproj.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <RootNamespace>MyGame.Tests</RootNamespace>
  </PropertyGroup>

  <!-- Pulls in 2dog.engine (the fixtures) and the compile-in collections -->
  <ItemGroup>
    <PackageReference Include="2dog.xunit" Version=":2dog-version:"/>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="3.*"/>
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*"/>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.*"/>
    <PackageReference Include="coverlet.collector" Version="10.*"/>
  </ItemGroup>

  <ItemGroup>
    <Content Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest"/>
    <ProjectReference Include="../MyGame.csproj"/>
  </ItemGroup>

  <!-- The Godot project is the parent directory; hosts nest inside it -->
  <PropertyGroup>
    <GodotProjectDir>..</GodotProjectDir>
    <TwoDogVariant Condition="'$(Configuration)' == 'Debug'">debug</TwoDogVariant>
    <TwoDogVariant Condition="'$(Configuration)' == 'Editor'">editor</TwoDogVariant>
    <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
  </PropertyGroup>

  <!-- Editor configuration: editor types become available -->
  <PropertyGroup Condition="'$(Configuration)' == 'Editor'">
    <DefineConstants>$(DefineConstants);EDITOR</DefineConstants>
  </PropertyGroup>
  <ItemGroup Condition="'$(Configuration)' == 'Editor'">
    <PackageReference Include="GodotSharpEditor" Version=":godot-version:"/>
  </ItemGroup>
</Project>
```

Because `GodotProjectDir` is set, [resource import](/import-tool) runs
automatically when the test project builds  –  tests see freshly imported
assets without a separate step.

## Running

```bash
dotnet test MyGame.xunit                     # Debug: template_debug engine
dotnet test MyGame.xunit -c Release          # template_release
dotnet test MyGame.xunit -c Editor           # editor build, TOOLS_ENABLED

dotnet test MyGame.xunit --filter "FullyQualifiedName~SceneTests"
```

| Configuration | Use it for |
| --- | --- |
| `Debug` | General unit tests and debugging |
| `Release` | Performance work and final validation |
| `Editor` | `[Tool]` scripts, `EditorInterface`, importer types |

For CI, `GodotHeadlessCollection` plus `GODOT_AUDIO_DRIVER=Dummy`.

## Traps Worth Knowing

- **Godot types in `[MemberData]`** crash the runner during discovery  –  use
  primitives or `DisableDiscoveryEnumeration = true`
  ([details](/known-issues/xunit-discovery)).
- **`GD.Print` output is hidden by default**
  ([details](/known-issues/gd-print-output)).
- **Nodes you add are not cleaned up for you.** The tree is shared across the
  collection: `QueueFree()` what you create, or assert against your own subtree.
