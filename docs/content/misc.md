# Misc

This page keeps low-level and contributor-facing details out of the main guides.
Start with [Adding 2dog](/add), [Hosts](/hosts/), [Resource Import](/import-tool),
or [Testing](/testing). The paths below are useful when the generated and
packaged flows are not enough; several are deliberately unsupported.

## Tool Edge Cases and Patch Internals

The supported command surface is in [Adding 2dog](/add). These details explain
what happens around unusual existing projects.

### Repeated and Named Hosts

Host options are repeatable and accept an optional folder name. Without one,
the tool picks a free name such as `MyGame.2dog`, then `MyGame.2dog2`:

```bash
2dog add --tests
2dog add --desktop MyGame.editor
```

A second desktop host can provide a level editor, dedicated server, or
benchmark entry point against the same game assembly. Each host gets a
`.gdignore`, a matching `DefaultItemExcludes` entry in the game project, and a
solution entry. Asking only for hosts that already exist is a no-op unless a
repeatable host option explicitly requests another one.

### Base-Name Resolution

The base name used for the game project, solution, and default host folders is
chosen in this order:

1. `[dotnet] project/assembly_name` in `project.godot`.
2. The sole root `.csproj` name.
3. A sanitized `application/config/name`.
4. The Godot project directory name.

`--name` overrides that derivation, but cannot conflict with an existing
`project/assembly_name`: Godot resolves `res://<assembly_name>.csproj`, so the
file and assembly must continue to agree.

### What Gets Patched

For an existing C# project, the tool targets .NET 10 and appends one marked
`<PropertyGroup>` with missing 2dog properties:

- `TargetFramework` (`net10.0`; existing values are updated in place)
- `EnableDynamicLoading`
- `AllowUnsafeBlocks`
- `LIBGODOT_ENABLED` in `DefineConstants`
- host paths in `DefaultItemExcludes`

Other project content is left unchanged. For a GDScript-only project, the tool
creates a `Godot.NET.Sdk` project and adds `[dotnet] project/assembly_name` to
`project.godot`.

Web setup adds `TwoDogWebBoot.cs`, a `Web` export preset when absent, and a root
`global.json` when none exists. An existing `export_presets.cfg` receives a new
preset at the next free index; existing presets are untouched. An existing
`global.json` is never overwritten, including with `--force`.

An existing root `.slnx` is reused. A classic `.sln` is converted to `.slnx`
and removed. If several root solutions contain the Godot project, the tool
fails instead of guessing. The web project is present in the solution but not
built by a normal solution build, so `wasm-tools` remains necessary only for
an explicit web publish.

Use `--dry-run` to inspect these actions. `--force` replaces scaffold-owned
files but still never moves or deletes game content, edits version control, or
overwrites `global.json`.

## Local Template and Contributor Workflows

**Contributor workflow.** Install the template directly from a checkout when
editing `templates/twodog/`:

```bash
dotnet new uninstall ./templates/twodog
dotnet new install ./templates/twodog
dotnet new 2dog -n TestApp
```

Uninstall and reinstall after template changes; `dotnet new` can otherwise use
the previously installed copy. `templates/twodog/` is the single source for
both consumption paths: `dotnet new 2dog` packages it under `content/twodog/`,
while the `2dog` tool embeds it and performs the same substitutions for
`2dog new` and `2dog add`. See the
[template contributor README](https://github.com/outfox/2dog/tree/main/templates).

For a fresh source checkout, initialize submodules and build native Godot before
the managed packages:

```bash
git submodule update --init --recursive
uv run poe build-all
```

The explicit build order is:

```bash
uv run poe build-godot
uv run poe build
```

`uv run poe build` packs platform packages, `2dog.engine`, `2dog.xunit`, and
the combined tool/template package, then restores the repository. A desktop or
web publish from project references also needs the Release import helper:

```bash
dotnet build twodog.import -c Release
```

This source-build path is for repository contributors. Package consumers should
use the ordinary commands in [Creating a New Project](/templates).

## Source Checkout and Native Layout

**Advanced and brittle.** A direct `ProjectReference` to `twodog.engine` does
not reproduce every transitive package behavior. Import its targets and select
the matching local platform project explicitly. For example, a Windows test
project can use:

```xml
<ItemGroup>
  <ProjectReference Include="..\twodog.engine\twodog.engine.csproj" />
</ItemGroup>

<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <TwoDogVariant>debug</TwoDogVariant>
</PropertyGroup>
<PropertyGroup Condition="'$(Configuration)' == 'Editor'">
  <TwoDogVariant>editor</TwoDogVariant>
</PropertyGroup>

<ItemGroup Condition="$([MSBuild]::IsOSPlatform('Windows')) And '$(Configuration)' == 'Debug'">
  <ProjectReference Include="..\platforms\twodog.win-x64\twodog.win-x64.debug.csproj"/>
</ItemGroup>
<ItemGroup Condition="$([MSBuild]::IsOSPlatform('Windows')) And '$(Configuration)' == 'Release'">
  <ProjectReference Include="..\platforms\twodog.win-x64\twodog.win-x64.release.csproj"/>
</ItemGroup>
<ItemGroup Condition="$([MSBuild]::IsOSPlatform('Windows')) And '$(Configuration)' == 'Editor'">
  <ProjectReference Include="..\platforms\twodog.win-x64\twodog.win-x64.editor.csproj"/>
</ItemGroup>

<Import Project="..\twodog.engine\build\2dog.engine.targets" />
```

Adjust relative paths and RID for the project location. With project references,
the compile-in xUnit collections are not imported automatically; reference the
`2dog.xunit` package or define a collection in the test assembly.

Native Godot expects different managed layouts:

```text
# editor (TOOLS_ENABLED)
app/GodotSharp/Api/Debug/GodotSharp.dll
app/GodotSharp/Api/Debug/GodotPlugins.dll

# template_debug and template_release
app/GodotSharp.dll
app/GodotPlugins.dll
```

`TwoDogVariant=editor` copies the editor layout. `debug` and `release` use the
flat layout. `TwoDogCopyGodotApi` handles build output and
`TwoDogPublishGodotApi` handles publish output; `GODOTSHARP_DIR` is pointed at
the selected location. If Godot reports `Unable to find the .NET assemblies
directory`, first confirm that `TwoDogVariant`, `libgodot-<variant>`, and this
layout agree. See [Build Variants](/build-configurations).

### NuGet Packaging Internals

`2dog.engine` contains `twodog.dll`, `GodotSharp.dll`, the Godot source
generator, MSBuild targets, GodotPlugins payload, and
`tools/net10.0/2dog.import.dll`. Native libraries remain in exact-pinned
platform packages:

- `2dog.win-x64`, `2dog.linux-x64`, and `2dog.osx-arm64` are meta packages.
- Their `.release`, `.debug`, and `.editor` packages carry one native variant.
- `2dog.tools` carries the GodotTools assemblies required by import.
- `2dog.browser-wasm` carries the browser native and publish targets.

The platform targets copy only the current OS and selected variant. Consumers
normally reference only `2dog.engine`; browser hosts additionally reference
`2dog.browser-wasm`, and tests normally reference `2dog.xunit`. Package version
selection is covered by [Configuration](/configuration#packages-and-versions).

## Advanced Desktop Frame Loop

The [console host](/hosts/console) shows the normal loop. Because the host owns
it, per-frame work may also operate directly on the live scene tree:

```csharp
var cubes = engine.Tree.CurrentScene
    .GetNode<Node3D>("Flair/WhiteCubes")
    .GetChildren().OfType<Node3D>().ToArray();

while (!godot.Iteration())
{
    var delta = (float)engine.Tree.Root.GetProcessDeltaTime();
    foreach (var cube in cubes)
        cube.Rotate(Vector3.Up, 1.8f * delta);
}
```

Godot still runs `_Process`, physics, rendering, and input during
`Iteration()`. Keep scene lifetime in mind: cached nodes become invalid if the
scene is replaced. `engine.Run(perFrame)` is the shorter equivalent when the
host does not need to own the loop body.

## Manual Browser Host Wiring

**Advanced; prefer `2dog add --web`.** Manual wiring must satisfy both the game
assembly and host sides described in [Browser Host](/hosts/web).

The Godot game project needs:

- `<DefineConstants>$(DefineConstants);LIBGODOT_ENABLED</DefineConstants>`
- `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
- `TwoDogWebBoot.cs` from the template, in the game assembly
- `export_presets.cfg` with a `Web` preset
- a solution beside `project.godot`, required by GodotTools during export
- a .NET 10 `global.json` whose SDK has `wasm-tools`

The web host needs `TargetFramework=net10.0`,
`RuntimeIdentifier=browser-wasm`, references to `2dog.engine`,
`2dog.browser-wasm`, and the game project, plus a `GodotProjectDir` pointing at
the game. Root the game and host assemblies because Godot finds scripts through
reflection:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MyGame"/>
  <TrimmerRootAssembly Include="$(TargetName)"/>
</ItemGroup>
```

For a nested host, add `.gdignore` inside the host and add `MyGame.web/**` to
the game project's `DefaultItemExcludes`. At runtime, register
`TwoDogWebBoot.PluginsInitializer()` before `Start()`, pass `null` as the
project path, and call `Run()` without disposing the engine. The full lifecycle
is in [Browser Host](/hosts/web#program).

## Import Helper and Native API

Automatic import is the supported path; see [Resource Import](/import-tool).
The bundled helper can also be run directly from a source checkout:

```bash
dotnet run --project twodog.import -- \
    --libgodot godot/bin/godot.windows.editor.x86_64.shared_library.dll \
    --api-dir godot/bin/GodotSharp/Api/Debug \
    --tools-dir godot/bin/GodotSharp/Tools \
    ./demos/showcase

dotnet run --project twodog.import -- --editor <godot-binary> ./demos/showcase
```

| Argument | Meaning |
| --- | --- |
| `<project-path>` | Directory containing `project.godot` |
| `--libgodot <path>` | Editor-variant shared library for in-process import |
| `--api-dir <dir>` | Directory containing `GodotPlugins.dll`; defaults to the helper directory |
| `--tools-dir <dir>` | Directory containing `GodotTools.dll`; required with `--libgodot` |
| `--editor <path>` | External editor subprocess; overrides `--libgodot` and falls back to `GODOT_EDITOR` |
| `--verbose` | Pass `--verbose` to Godot |

The helper locks `.godot/2dog.import.lock` so builds importing one project do
not race. Template natives cannot import; `--libgodot` requires an editor
variant with `TOOLS_ENABLED`.

**Low-level and unsafe to combine with a running engine.** The Godot fork's
editor build exports:

```c
LIBGODOT_API int libgodot_import_project(const char *p_project_path,
        int p_extra_argc, const char *p_extra_argv[]);
```

It performs the complete `godot --headless --import --path <project>` lifecycle
and returns a process-style exit code. It returns `-1` when editor support is
absent. Never call it while an embedded Godot instance exists; 2dog invokes it
in a fresh helper process for that reason.

## Experimental Parallel xUnit Engines

**Experimental, unpublished, and unsupported on macOS.** Normal tests must use
the non-parallel collections in [Testing](/testing). The in-repository
`twodog.hosting`, `twodog.hosting.runtime`, and `twodog.hosting.xunit` projects
can isolate one native copy and assembly load context per collection. They are
not NuGet packages; use project references and consult the
[parallel collections demo](https://github.com/outfox/2dog/tree/main/demos/xunit/parallel_collections).

Each collection derives a distinct `EngineInstanceFixture` and deliberately
omits `DisableParallelization`:

```csharp
using Godot;
using twodog.Hosting;
using twodog.Hosting.Runtime;
using twodog.Hosting.Xunit;
using Xunit;

public sealed class AlphaEngineFixture : EngineInstanceFixture
{
    protected override string Tag => "alpha";
}

[CollectionDefinition(nameof(AlphaCollection))]
public sealed class AlphaCollection : ICollectionFixture<AlphaEngineFixture>;

public sealed class AddNodeScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument)
    {
        var root = session.Tree.Root!;
        root.AddChild(new Node { Name = $"n_{argument}" });
        session.PumpFrames(1);
        return $"children={root.GetChildCount()}";
    }
}

[Collection(nameof(AlphaCollection))]
public sealed class AlphaTests(AlphaEngineFixture fixture)
{
    [Fact]
    public void RunsAgainstItsOwnEngine()
    {
        Assert.SkipWhen(!EngineHost.IsSupported,
            "In-process hosting is unsupported on this platform.");
        Assert.Contains("children=", fixture.Run<AddNodeScenario>("alpha"));
    }
}
```

Godot types cannot cross the load-context boundary; scenarios run on the
engine thread and return a report string. Override `Tag`, `SourceProjectDir`,
`EngineArgs`, `Variant`, or timeout settings as needed. The working directory,
environment, signal handlers, exception handlers, and native crash blast
radius remain process-global. Use separate processes when those boundaries
matter. Check `EngineHost.IsSupported` before invoking the fixture.

## Module-Initializer Discovery Workaround

**Risky; prefer primitive `[MemberData]` values or
`DisableDiscoveryEnumeration = true`.** Those supported workarounds are in
[xUnit Test Discovery](/known-issues/xunit-discovery). If discovery itself must
construct Godot values, a module initializer can start the engine as the test
assembly loads:

```csharp
using System.Runtime.CompilerServices;
using Godot;
using twodog;

namespace MyGame.Tests;

internal static class TestInitializer
{
    private static Engine? _engine;
    private static GodotInstance? _godot;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_engine != null) return;

        _engine = new Engine("tests", Engine.ResolveProjectDir(), "--headless");
        _godot = _engine.Start();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _godot?.Dispose();
            _engine?.Dispose();
        };
    }
}
```

This has assembly-load side effects, slows IDE discovery, and relies on
`ProcessExit` cleanup. It must live in the test project. Do not combine it with
`GodotFixture` or `GodotHeadlessFixture`; both would try to start the one
allowed engine and produce `InvalidOperationException`. Use this workaround
only when preserving individually discovered Godot-valued rows outweighs
those costs. A little more ceremony keeps this dog from biting the test host.
