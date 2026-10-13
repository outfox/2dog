---
title: Build Variants
description: "Choosing native debug, release and editor variants, and building matching single- or double-precision 2dog packages."
---

# Build Variants

2dog selects the native Godot variant from your .NET build configuration.

| .NET configuration | Default variant | Godot build | `TOOLS_ENABLED` | Use |
| --- | --- | --- | --- | --- |
| **Debug** | `debug` | `template_debug` | No | Development, debugging, tests |
| **Release** | `release` | `template_release` | No | Production and final validation |
| **Editor** | `editor` | `editor` | Yes | Editor types and `[Tool]` scripts |

`debug` includes assertions and debugging support; `release` is the optimized
production runtime; `editor`
enables `TOOLS_ENABLED`, including editor types, import plugins, and `[Tool]`
scripts.

::: tip Opening the editor
The editor variant enables editor APIs; pass `--editor` to start the interactive
editor and initialize editor runtime singletons such as `EditorInterface`:

```bash
dotnet run --project MyGame.2dog -c Editor -- --editor
```

The generic host forwards these arguments and pumps the editor's main loop.
Editor builds and publishes copy GodotTools and its dependencies into
`GodotSharp/Tools` beside the host executable. Custom hosts must keep pumping
the engine and avoid assuming that a game scene is running in editor mode.

Resource import also runs separately and automatically at build time; see
[Resource Import](./import-tool).
:::

## Selecting a Variant

`TwoDogVariant` defaults to `debug` in Debug, `editor` in Editor, and `release`
for Release and other configurations. Set it explicitly to override that mapping:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <TwoDogVariant>debug</TwoDogVariant>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog.engine" Version=":2dog-version:"/>
  </ItemGroup>
</Project>
```

All three desktop variants come through the platform meta package, so selecting
a variant needs no extra package references. `TwoDogVariant` also controls the copied
native library, GodotPlugins layout, and embedded runtime metadata.

Web hosts use `TwoDogWebVariant` with the same configuration mapping. The
`2dog.browser-wasm` package restores both debug and release natives; a missing
selected payload fails the build. Web and Android do not support the editor
variant: use Debug or Release, or explicitly select a supported variant.

Without an explicit variant override, use the configuration in the usual way:

```bash
dotnet run -c Debug
dotnet publish -c Release
dotnet test -c Editor
```

If Godot reports that it cannot find its .NET assemblies, confirm that
`TwoDogVariant` is `release`, `debug`, or `editor`, then clean and rebuild:

```bash
dotnet clean
dotnet build -c Debug
```

See [Configuration](./configuration) for the property and package reference.

## Double Precision

Double precision is independent of the Debug, Release and Editor variants.
Build a matching native and managed distribution from a separate checkout or
worktree:

```bash
uv run poe build-double
```

This builds the host editor and native variants with `precision=double`,
generates C# glue with that editor, builds the assemblies with
`--precision=double`, and packs the affected packages with a `.double` suffix.
Use `uv run poe pack-double` to repack after an existing double glue build.
The default build tasks continue to produce single-precision packages.

For individual builds, use `uv run build-godot.py --precision double` and
`dotnet pack <project> -p:TwoDogPrecision=double`. Double payloads are staged
under `godot/bin/double/`, and managed intermediate/output directories are
separate from the single-precision build. Generated Godot glue is shared within
a checkout, so regenerate it when changing precision. Source builds reject
bindings with the wrong scalar type using `TDG006`, preventing incorrectly
labeled packages after a precision switch.

| Packages | Double build required | Reason |
| --- | --- | --- |
| Desktop, browser and Android native variants and their meta packages | Yes | Native `real_t` layout and matching variant dependencies |
| `2dog.godotsharp`, `2dog.godotsharp.editor` | Yes | Math struct layout, interop code and generated API signatures |
| `2dog.tools` | Yes | GodotTools compiles against the matching editor/core bindings |
| `2dog.engine` | Yes | Godot APIs, bundled GodotPlugins/import helper, native and binding dependencies |
| `2dog.avalonia`, `2dog.blazor`, `2dog.nunit`, `2dog.repl` | No | Stable managed signatures; the host explicitly selects the engine package |
| `2dog.xunit` | No | Helpers compile in the host against its selected bindings |
| Stock `Godot.NET.Sdk` | No | The precision setting and fork binding replacement come from the double packages |
| `2dog` CLI and templates | No | The tool has no Godot API/native dependency; scaffolded references require the changes below |
| `2dog.android` | No | Java-only payload and host targets; JNI names remain the same |

Append `.double` to the **entire** affected package ID, for example
`2dog.win-x64.double` and `2dog.win-x64.release.double`. Managed package
versions still use `:2dog-version:`; native packages and `2dog.tools.double`
use `:natives-version:`.

Reference the double bindings in every game/library project and the double
engine in every host. Integration packages keep their ordinary names and do
not pull in an engine transitively. Keep integrations and the selected engine
on the same managed version. For example, an Avalonia host references:

```xml
<ItemGroup>
  <PackageReference Include="2dog.engine.double" Version=":2dog-version:" />
  <PackageReference Include="2dog.avalonia" Version=":2dog-version:" />
</ItemGroup>
```

The game references `2dog.godotsharp.double` and, when needed,
`2dog.godotsharp.editor.double`. Keep `2dog.avalonia`, `2dog.blazor`,
`2dog.xunit`, `2dog.nunit` and `2dog.repl` unchanged. Switch explicitly
referenced native platform packages to their `.double` counterparts.
Avalonia uses the engine's stable `GodotMath.CreateVector2(double, double)`
bridge; the precision-specific constructor call stays inside the engine.
Rebuild your game and all libraries that use Godot APIs against the double
bindings. Third-party binaries compiled against single-precision Godot may
require their own double builds. GDExtensions must also match the engine's
precision.

The packages set `GodotFloat64` and `REAL_T_IS_DOUBLE` for consumers and reject
mixed precision packages with `TDG005` before compilation. Set
`TwoDogPrecision=double` explicitly when building this repository, for example:

```bash
dotnet test 2dog.tests.slnf -c Debug -p:TwoDogPrecision=double
uv run tests/bindings/test_packages.py --precision double
```

The experimental hosting projects are not currently packaged. Rebuild
`twodog.hosting.runtime` and its consumers with `TwoDogPrecision=double`; the
shared `twodog.hosting` contract has no Godot API dependency.

The CLI's scaffolding and package-update commands currently select standard
package IDs. After scaffolding, change the references as described above;
avoid using `2dog update` to manage a double project's package identities.

Cross-compiled web and Android builds also accept `--precision double`.
They need a prior desktop double glue build for bindings and resource import.
The local package tasks use the same empty non-host native stubs as the normal
build; build real payloads on each target platform before distributing them.

### Double-Precision Showcase

The repository includes a desktop host with an explicit
`2dog.engine.double` reference. Once matching double packages are available
in the local feed, run it from the repository root:

```bash
dotnet run --project demos/showcase/showcase.double
dotnet run --project demos/showcase/showcase.double -c Release
```

For a short headless check:

```bash
dotnet run --project demos/showcase/showcase.double -- --headless --quit-after 10
```

The host selects double precision without a command-line property. Its
separate game project compiles the existing showcase scripts against
`2dog.godotsharp.double`, while sharing the same scenes and resources.
Both projects have separate intermediate and assembly output directories,
so the standard showcase can still build against single precision.

Startup checks verify native double support, large-coordinate math and
Variant marshalling, then print `2DOG_API_SMOKE_PASSED`. The example lives in
[`demos/showcase/showcase.double`](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.double).

### CI Coverage

CI builds and tests both precisions. Native cache releases use
`godot-<submodule-hash>` for single precision and
`godot-<submodule-hash>-double` for double precision. Missing releases trigger
native builds for Linux x64, Windows x64, macOS arm64, browser wasm32 and
Android arm64/x64. SCons caches and workflow artifacts also include precision.
The native workflow's manual `precision` input selects which family to rebuild.

Each precision runs desktop tests in Debug, Release and Editor, desktop
publish and Native AOT smoke tests, Web/WebXR/Blazor browser smoke tests, and
Android APK build/inspection and emulator tests. Browser AOT runs on release
tags and the `double-precision` branch. Shared runtime probes check Godot's
precision feature and math marshalling, so single natives cannot silently pass
a double smoke test. CLI scaffolding smoke tests use standard packages.

CI requires every platform payload before packing. Local empty native stubs
are not accepted by the release staging step. Version-tag deployment requires
both precision matrices to pass and retains the standard build's shared
integration packages when combining the two feeds.

### Limiting Local Build Parallelism

Set the machine's `TWODOG_MAX_BUILD_JOBS` environment variable to a positive
integer to cap native builds and the managed pack/restore commands in the Poe
build tasks. The effective limit is capped at the machine's processor count.
When unset, existing tool defaults apply. On Windows, the build scripts also
read the user environment setting so it takes effect in existing terminals.
Direct `dotnet` commands can use `-maxcpucount:20`, and direct SCons commands
can use `--jobs=20`.
