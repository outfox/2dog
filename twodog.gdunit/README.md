# 2dog.gdunit

Run GDScript gdUnit tests in a private copy of a Godot project with embedded
libgodot. Microsoft.Testing.Platform handles discovery, test selection and results.

The package includes the unmodified runtime from the official
[GdUnit 6.2.2 release](https://github.com/godot-gdunit-labs/gdUnit4/releases/tag/v6.2.2),
its MIT license, and the embedded bridge. Consumers need the .NET 10 SDK and
the normal `2dog.engine` native and
editor-tool dependencies. No separate Godot executable or addon installation is
needed. Use `2dog.gdunit.double` for a double precision game.

## Create a host

Under a game directory containing `project.godot`, create `MyGame.gdunit/`, put
an empty `.gdignore` in it, and add `tests/`. Create `MyGame.gdunit.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MyGame.GdUnit</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="2dog.gdunit" Version="YOUR_2DOG_VERSION" />
    <AssemblyMetadata Include="GodotProject" Value="$(MSBuildProjectDirectory)/.." />
  </ItemGroup>
</Project>
```

Replace `YOUR_2DOG_VERSION` with the version used by the game. For a mixed C#
game, add a `ProjectReference` to its project so the game assembly and its
dependencies are copied into the host output. Exclude `MyGame.gdunit/**` from
the game's C# compile glob.

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

`AddSelfRegisteredExtensions` is generated in the project's `RootNamespace` and
registers MTP extensions referenced by the host, including MSBuild integration.

Place this `global.json` in `MyGame.gdunit/` to select the .NET 10 MTP runner for
commands issued inside that directory:

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

Add `tests/example_test.gd`:

```gdscript
extends GdUnitTestSuite


func test_answer() -> void:
	assert_int(21 * 2).is_equal(42)
```

## Run and select tests

```text
cd MyGame.gdunit
dotnet test --project MyGame.gdunit.csproj
dotnet test --project MyGame.gdunit.csproj --list-tests
dotnet test --project MyGame.gdunit.csproj --gdunit-filter "*test_answer"
```

`--gdunit-filter` matches gdUnit's fully qualified name, including a parameter
row's display name. Matching is case-sensitive; `*` and `?` are wildcards. MTP's
`--filter-uid` selects exact test IDs. IDs depend on the resource path, method,
and row index, so a newly imported stage has the same IDs. `--list-tests json`
includes IDs and original source locations. Native Godot output can appear
before or after the JSON document when the executable is run directly; parse
the JSON document separately from those native messages.

Discovery imports a private stage and starts Godot, including game autoloads,
but executes no test bodies or suite hooks. Running uses the same discovery
session and executes only selected cases. Empty selection follows MTP's no-test
policy. Assertions, skips, test errors, and suite teardown errors affect MTP
results; native warnings/errors also fail the host.

## Execution options

Set these properties on `GdUnitOptions` in `Program.cs`:

| Property | Default | Purpose |
| --- | --- | --- |
| `ProjectPath` | Required | Directory containing `project.godot` |
| `TestRoots` | `tests` | Directories relative to the game |
| `ArtifactsDirectory` | Output `TestResults/gdunit` | Parent of fresh stages and logs |
| `ProjectAssemblyDirectory` | Host output | Built game assemblies and dependencies |
| `ImportTimeout` | Two minutes | Limit for the separate import process |
| `SessionTimeout` | Five minutes | Limit for pumping the embedded session |

Each request keeps `project/`, `import.log`, `godot.log`, and `events.json` in a
fresh artifact directory. The source game and its import cache are unchanged.
Selected test roots become visible only in the stage; nested `.gdignore` trees
remain excluded. Symlinks and an existing game copy of `addons/gdUnit4` are
rejected. Source edits appear in the next fresh stage.

The host is headless and serializes requests within its load context. It pumps
and destroys Godot on the thread that started it, then waits for engine
completion. MTP cancellation interrupts the import process or asks gdUnit to
terminate the session and finish teardown, with a five-second cleanup grace
period. A host timeout or cancellation cannot interrupt a blocked native call.

CLI scaffolding, rendering support, snapshot caching, relocatable published
hosts, and verified IDE integration remain future work. The adapter depends on
GdUnit session internals at the pinned release; rerun its compatibility tests
when updating that release. See [MTP's dotnet test documentation](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)
for runner selection and extensions.

## Building this package

`dotnet pack twodog.gdunit` downloads the official release archive pinned in
`GdUnitRuntime.props` and checks its SHA-256 before extraction. The first pack
needs access to GitHub; later packs reuse the verified archive under
`obj/gdunit-release/<precision>/<version>/`. No GdUnit checkout is required.
Each package records the release version, archive URL and checksum in
`runtime/gdunit-release.json`.

To update GdUnit, change the version and checksum together in
`GdUnitRuntime.props`, repack, and run the integration and MTP contracts in
Debug, Release and Editor. Builds use this explicit pin rather than resolving
a moving latest-release URL.
