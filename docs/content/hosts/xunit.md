---
title: xUnit Host
description: "The 2dog test host: xUnit owns the process, so a fixture owns the Godot engine lifetime - test anatomy, project differences, and single-instance safety."
---

# xUnit Host

`MyGame.tests` runs tests against a real Godot engine, project, and resources.
Because xUnit owns the process, an xUnit fixture owns the engine lifetime.

## Use It

```bash
dotnet test MyGame.tests
```

::: info This page covers host anatomy
[Testing with xUnit](/testing) is the canonical guide to fixtures, engine
arguments, collections, filtering, and CI workflows.
:::

## Capabilities

- Tests scenes, resources, and game code against a real Godot engine.
- Supports headless tests by default and rendered tests when needed.
- Supports the same debug, release, and editor variants as the generic host.
- Imports changed resources automatically before tests run.

## How It Works

```csharp
using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit;

namespace MyGame.Tests;

[Collection<HeadlessCollection>]
public class BasicTests(HeadlessFixture godot)
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

The fixture exposes the same objects a generic host keeps in local variables:

| Fixture member | Console equivalent |
| --- | --- |
| `godot.Engine` | `new Engine(...)` |
| `godot.GodotInstance` | `engine.Start()` |
| `godot.Tree` | `engine.Tree` |

## Project Setup

The shared host project is documented in [Hosts](./). The test host references
`2dog.xunit`, xUnit, the test SDK, and `MyGame.csproj`. `2dog.xunit` brings in
`2dog.engine`, fixtures, and collection definitions.

```xml
<ItemGroup>
  <PackageReference Include="2dog.xunit" Version=":2dog-version:"/>
  <PackageReference Include="xunit.v3" Version="3.*"/>
  <PackageReference Include="xunit.runner.visualstudio" Version="3.*"/>
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.*"/>
  <PackageReference Include="coverlet.collector" Version="10.*"/>
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="../MyGame.csproj"/>
</ItemGroup>

<PropertyGroup>
  <GodotProjectDir>..</GodotProjectDir>
  <TwoDogVariant Condition="'$(Configuration)' == 'Debug'">debug</TwoDogVariant>
  <TwoDogVariant Condition="'$(Configuration)' == 'Editor'">editor</TwoDogVariant>
  <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
</PropertyGroup>

<PropertyGroup Condition="'$(Configuration)' == 'Editor'">
  <DefineConstants>$(DefineConstants);EDITOR</DefineConstants>
</PropertyGroup>

<ItemGroup Condition="'$(Configuration)' == 'Editor'">
  <PackageReference Include="GodotSharpEditor" Version=":godot-version:.*"/>
</ItemGroup>
```

`GodotProjectDir` enables [automatic resource import](/import-tool), so tests
see freshly imported assets. Debug, Release, and Editor configurations select
the matching native variant; see [Build Variants](/build-configurations).

## Limitations

The generated host sets `"parallelizeTestCollections": false` in
`xunit.runner.json`: one engine runs at a time, and every test in a collection
shares it. [Testing with xUnit](/testing#collections) covers collections and
the single-instance rule.

Remember that nodes added to the shared tree are not cleaned up automatically;
`QueueFree()` what you create. See the known issues for
[Godot types in `MemberData`](/known-issues/xunit-discovery) and
[`GD.Print` output](/known-issues/gd-print-output).
