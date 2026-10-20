---
title: GdUnit Configuration
description: "Configure GdUnit host packages, private resource roots, execution limits and artifacts."
tagline: "Learn how to configure GdUnit runs, limits and test artifacts."
---

# GdUnit Configuration

The [GdUnit host](/hosts/gdunit) uses the .NET 10 SDK and MTP runner. Reference
`2dog.gdunit` at the same 2dog version as the game. Choose
`2dog.gdunit.double` for a double precision game. The package brings the engine
and editor-tool dependencies needed for private resource imports, together with
a pinned official GdUnit runtime. Consumers do not need a GdUnit checkout or a
separate addon installation.

Debug, Release and Editor select the corresponding
[engine variant](/build-configurations). The host always runs headlessly.
Normal game importing and pack export are disabled for the host project;
discovery and execution import their own private stages.

## Create the host

From the directory containing `project.godot`, run:

```bash
dnx 2dog add --gdunit
```

Use `--gdunit MyGame.scripts` to choose a custom host folder. New projects
can use `dnx 2dog new MyGame --gdunit`; the template accepts `--gdunit true`.
The tool creates this layout, including a sample `tests/example_test.gd`:

```text
MyGame.gdunit/
  .gdignore
  global.json
  MyGame.gdunit.csproj
  Program.cs
  tests/
```

Leave `.gdignore` empty. It keeps the host and its test resources out of the
game's normal import and export. The host imports a private copy when running.

## Project and entry point

For manual setup, create `MyGame.gdunit/MyGame.gdunit.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MyGame.GdUnit</RootNamespace>
    <GodotProjectDir>..</GodotProjectDir>
    <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="2dog.gdunit" Version=":2dog-version:" />
    <PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" Version="18.*" />
    <AssemblyMetadata Include="GodotProject" Value="$(MSBuildProjectDirectory)/.." />
  </ItemGroup>
</Project>
```

For a C# game, add a `ProjectReference` to `../MyGame.csproj` so its built
assembly and dependencies reach the host output. Exclude `MyGame.gdunit/**`
from the game's C# compile glob.

Add `Program.cs`:

```csharp
using System.Reflection;
using Microsoft.Testing.Platform.Builder;
using MyGame.GdUnit;
using twodog.GdUnit;

var project = Assembly.GetExecutingAssembly()
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "GodotProject").Value!;
var builder = await TestApplication.CreateBuilderAsync(args);
builder.AddSelfRegisteredExtensions(args);
builder.AddGdUnit(new GdUnitOptions
{
    ProjectPath = project,
    TestRoots = ["MyGame.gdunit/tests"]
});
using var application = await builder.BuildAsync();
return await application.RunAsync();
```

`AddSelfRegisteredExtensions` is generated in the project's `RootNamespace`;
`AddGdUnit` registers the GDScript test framework.

## Select the runner

Besides the root `global.json` described in
[Test runners](/testing/#test-runners), the scaffold puts this one inside
`MyGame.gdunit/`:

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

With the root file in place, run commands from the project root:

```bash
dotnet test MyGame.gdunit
dotnet test --project MyGame.gdunit --list-tests
```

If your root `global.json` does not select MTP, run from `MyGame.gdunit/`
instead.

## Execution options

Set these properties on `GdUnitOptions` in `Program.cs`:

| Property                   | Default                          | Purpose                                |
|----------------------------|----------------------------------|----------------------------------------|
| `ProjectPath`              | Required                         | Directory containing `project.godot`   |
| `TestRoots`                | `tests`                          | Directories relative to the game       |
| `ArtifactsDirectory`       | Host output `TestResults/gdunit` | Parent of fresh stages and logs        |
| `ProjectAssemblyDirectory` | Host output                      | Built game assemblies and dependencies |
| `ImportTimeout`            | Two minutes                      | Limit for the separate import process  |
| `SessionTimeout`           | Five minutes                     | Limit for pumping one embedded session |

For tests under a nested host, include that host in each root, for example
`MyGame.gdunit/tests`. Roots must remain inside the source game. Selected roots
can be ignored in the game; nested `.gdignore` trees remain excluded. Linked
files and directories are unsupported. Timeouts must be positive.

For a mixed C#/GDScript game, build the game through a `ProjectReference` from
the host. Keep `ProjectAssemblyDirectory` at the host output unless its game
assemblies and dependencies are staged elsewhere.

## Artifacts

Each request creates a fresh subdirectory beneath `ArtifactsDirectory`:

| Artifact      | Contents                                                        |
|---------------|-----------------------------------------------------------------|
| `project/`    | Writable game copy, selected tests, pinned framework and bridge |
| `import.log`  | Resource import helper output                                   |
| `godot.log`   | Embedded engine's native log                                    |
| `events.json` | Serialized discovery, lifecycle and result events               |

Reports from GdUnit's session hooks live under
`project/twodog_test_host/reports/`. The host retains artifacts for inspection.
On Windows, choose a short artifact path for deeply nested projects because
native directory operations can reach Windows path length limits.

For assertions, suites and other framework settings, use
[GdUnit's official documentation](https://godot-gdunit-labs.github.io/gdUnit4/latest/).
See [Testing with GdUnit](/testing/gdunit) for the host's execution differences.
