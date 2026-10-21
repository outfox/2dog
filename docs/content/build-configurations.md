---
title: Build Variants
description: "Choosing debug, release and editor variants with single or double precision."
tagline: "Learn how to pick debug, release or editor builds, in single or double precision."
---

# Build Variants

2dog selects the native Godot variant from your .NET build configuration:

| .NET configuration | `TwoDogVariant` | Use                               |
|--------------------|-----------------|-----------------------------------|
| **Debug**          | `debug`         | Development, debugging, tests     |
| **Release**        | `release`       | Production                        |
| **Editor**         | `editor`        | Editor types and `[Tool]` scripts |

Every variant also comes in [double precision](#double-precision);
`TwoDogVariant` stays the same.

`debug` includes assertions and debugging support; `release` is the optimized
production runtime; `editor`
enables `TOOLS_ENABLED`, including editor types, import plugins, and `[Tool]`
scripts.

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
a variant needs no extra package references.

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

See [Configuration](./configuration) for the property and package reference.

## Double Precision

Reference `2dog.engine.double` in the host and `2dog.godotsharp.double` in
every game or library that uses Godot APIs. Editor builds also use
`2dog.godotsharp.editor.double`. Rebuild those projects against the matching
bindings; single- and double-precision Godot types cannot be mixed.

```xml
<PackageReference Include="2dog.engine.double" Version=":2dog-version:" />
```

Keep `2dog.avalonia`, `2dog.blazor`, `2dog.xunit`, `2dog.nunit` and `2dog.repl`
under their ordinary names; `2dog.gdunit` becomes `2dog.gdunit.double`. Native dependencies are selected automatically;
if referenced explicitly, append `.double` to the entire ID, such as
`2dog.win-x64.release.double`. 

::: danger GDExtensions + Double Precision
GDExtensions must also match the precision!
:::

## Experimental WebGPU

WebGPU is opt-in and currently supports the Mobile renderer on single-precision
Windows, Linux and macOS, and single-threaded browser runtimes. Existing renderer
defaults are preserved. Android, double precision, threaded browser payloads and
Forward+ reject WebGPU with a clear error. Native threading remains supported.
Headless imports and exports do not require a WebGPU device.

```bash
dotnet run --project MyGame.2dog -- --rendering-driver webgpu --rendering-method mobile
```

For project settings, select both the renderer and the platform driver:

```ini
[rendering]
renderer/rendering_method="mobile"
rendering_device/driver="webgpu"
rendering_device/driver.windows="webgpu"
rendering_device/driver.linuxbsd="webgpu"
rendering_device/driver.macos="webgpu"
```

Configure `.web` and `.mobile` feature overrides explicitly when present.
See [web configuration](/configuration/web#experimental-webgpu) and
[Blazor configuration](/configuration/blazor#experimental-webgpu) for browser boot.
WebXR's immersive rendering is outside this experimental scope.

### Adapter and shader limits

The backend has no image atomics, multiview or ray tracing. Read/write storage
textures require native support for their format; unsupported formats fail
shader creation. Use supported R32 formats or separate read and write resources.
Overlapping writable storage-buffer bindings are rejected instead of redirected
to dummy buffers. GPU timestamp and memory statistics are not implemented.
Translated WGSL is cached; GPU pipeline compilation still happens at runtime.

Native Dawn loads only when selected. WebGPU requires macOS 13 or newer; other
renderers retain their existing runtime requirements. Desktop packages include
Dawn and the compiler sidecars and notices needed by the selected backend.

### Source builds

Normal `uv run poe build-all` enables WebGPU on supported configurations. Keep
the Emscripten SDK pinned to `EmscriptenVersion` from `Directory.Build.props`.

```text
uv run build-godot.py --webgpu auto
uv run build-godot.py --platform web --webgpu yes
```

Use `--webgpu no` to omit the driver. `--webgpu-native-path <sdk>` supplies an
existing native Dawn boundary SDK, including its runtime libraries and licenses.
Unsupported configurations reject `--webgpu yes`; `auto` preserves their renderer.
Browser packages supply a prepared port and a writable cache. Override that cache
with `TwoDogWebGpuCachePath` when needed.

## Downloading CI Packages

In a 2dog checkout, use the current branch's tested packages without compiling
Godot. Install `uv`, the .NET SDK selected by `global.json`, and an
authenticated GitHub CLI (`gh`), then run:

```bash
uv run poe pull-packages
dotnet run --project demos/showcase/showcase.2dog
```

The command imports the latest successful CI run with available packages for
the current branch into `packages/`. Each bundle includes Windows x64, Linux
x64, macOS arm64, browser and Android packages. It also downloads the matching
Godot SDK, managed APIs and source generator, refreshes only stale package
versions in the checkout's `.packages/` cache, and restores `2dog.tests.slnf`.
Use the same command in the checkout on each operating system.

Pass `--run <run-id>` to select the same snapshot on several machines,
`--precision double` or `--precision both` for double precision, `--plan` to
show the run, sizes and expiry without downloading, or `--no-restore` to
import the feed without restoring projects. WebGPU uses single precision.

The downloader checks package versions against `Directory.Build.props` and
the native source against the checkout's Godot submodule commit. It rejects
incomplete feeds and empty native stubs. The selected run and package versions
are recorded in `packages/ci-packages.json`.

CI package artifacts expire after seven days. If no matching artifact remains,
run CI again. Avoid `poe build` after downloading unless you intend to repack:
without local native builds it can replace other platforms' packages with
empty stubs. `dotnet build`, `dotnet run` and `dotnet test` use the downloaded
feed directly.

For a project outside the checkout, add the absolute path to `packages/` as a
NuGet source in that project's `NuGet.Config`, and pin the versions from the
downloaded feed. If the project previously restored different contents at
those same versions, remove only the affected package/version directories
from its NuGet cache before restoring.

## Editor Variants

::: tip Opening the editor
The editor variant enables editor APIs; pass `--editor` to start the interactive
editor and initialize editor runtime singletons such as `EditorInterface`:

```bash
dotnet run --project MyGame.2dog -c Editor -- --editor
```

The generic host forwards these arguments and pumps the editor's main loop.
Custom hosts must keep pumping the engine and avoid assuming that a game scene
is running in editor mode.

Resource import also runs separately and automatically at build time; see
[Resource Import](./import-tool).

The editor's export dialog edits the presets that `dotnet publish` uses. It
cannot build a playable game, because the host does that: Export Project,
Export All, one-click deploy and `--export-release`/`--export-debug` are
unavailable inside a host. PCK/ZIP and patch exports remain and contain
resources only; see [Pack export](./import-tool#pack-export).
:::

