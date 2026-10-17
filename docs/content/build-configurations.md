---
title: Build Variants
description: "Choosing debug, release and editor variants with single or double precision."
---

# Build Variants

2dog selects the native Godot variant from your .NET build configuration.
Each desktop variant is available with single or double precision:

| .NET configuration | `TwoDogVariant` | Precision | Use |
| --- | --- | --- | --- |
| **Debug** | `debug` | Single | Development, debugging, tests |
| **Release** | `release` | Single | Production |
| **Editor** | `editor` | Single | Editor types and `[Tool]` scripts |
| **Debug** | `debug` | Double (`.double` packages) | Development, debugging, tests |
| **Release** | `release` | Double (`.double` packages) | Production |
| **Editor** | `editor` | Double (`.double` packages) | Editor types and `[Tool]` scripts |

Single precision is the default. Select double precision through the
[package references below](#double-precision); `TwoDogVariant` stays the same.

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

Reference `2dog.engine.double` in the host and `2dog.godotsharp.double` in
every game or library that uses Godot APIs. Editor builds also use
`2dog.godotsharp.editor.double`. Rebuild those projects against the matching
bindings; single- and double-precision Godot types cannot be mixed.

```xml
<PackageReference Include="2dog.engine.double" Version=":2dog-version:" />
```

Keep `2dog.avalonia`, `2dog.blazor`, `2dog.xunit`, `2dog.nunit` and `2dog.repl`
under their ordinary names. Native dependencies are selected automatically;
if referenced explicitly, append `.double` to the entire ID, such as
`2dog.win-x64.release.double`. 

::: danger GDExtensions + Double Precision
GDExtensions must also match the precision!
:::

## WebGPU Branch

The `webgpu` branch includes native Dawn/WebGPU in single-precision desktop
packages and WebGPU in single-threaded browser packages. Existing renderer
defaults are preserved. An explicit WebGPU driver argument defaults to Forward+
when no rendering method is supplied. Both Forward+ and Mobile are available;
Vulkan, D3D12 and Metal retain their existing rendering methods. Forward+ is
experimental, with image checks for clustered lighting, shadows, SSAO,
volumetric fog, bounced SDFGI and MSAA. Broader feature and hardware parity is
still being tested.
Double precision, threaded web,
Android, and WebXR retain their existing renderer paths.

Godot falls back to Mobile if the adapter exposes fewer than 48 sampled
textures per shader stage. On adapters without the optional storage-format
tiers, unsupported read/write formats use read snapshots refreshed between
compute dispatches. Custom storage-image shaders that need to read their own
writes within one dispatch require native support for that format.

Native Dawn loads only when WebGPU is selected; existing renderers keep their
original runtime requirements. WebGPU requires macOS 13 or newer on macOS and
the [Visual C++ Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe)
on Windows.

Select WebGPU Forward+ in a desktop host with Godot's arguments:

```bash
dotnet run --project MyGame.2dog -- --rendering-driver webgpu --rendering-method forward_plus
```

To select it through `project.godot` instead, configure both the driver and
the rendering method. Platform driver overrides take precedence over the base
driver setting:

```ini
[rendering]
renderer/rendering_method="forward_plus"
rendering_device/driver="webgpu"
rendering_device/driver.windows="webgpu"
rendering_device/driver.linuxbsd="webgpu"
rendering_device/driver.macos="webgpu"
```

The `.mobile` suffix in `renderer/rendering_method.mobile` is a platform feature
override; it does not select the Mobile renderer on desktop. Configure existing
`.web` or `.mobile` overrides separately for those platforms. To keep using
Mobile, select `"mobile"` in the project or pass `--rendering-method mobile`.
`--editor` opens the editor using the selected renderer and driver.

The platform package copies Dawn and its dependencies beside libgodot for
both build and publish. Keep those files and their `licenses/` directory when
deploying. Browser packages carry the pinned Emscripten port and supply the
final-link flags automatically. See [Web configuration](/configuration/web)
for browser initialization and [Blazor configuration](/configuration/blazor)
for the view parameter.

Source builds enable WebGPU automatically on supported configurations:

```bash
uv run build-godot.py
uv run build-godot.py --platform web
uv run poe build
```

The builder verifies the pinned Dawn and DXC downloads and builds a small
shared C API library around Dawn. It requires a C++20 compiler and prepares
its pinned CMake through `uv`. The official Windows Dawn SDK also requires
Visual Studio 2026 with MSVC Build Tools 14.51 or newer; the native CI installs that
toolset when the runner lacks it. An existing SDK can be selected with
`--webgpu-native-path <sdk>`; it must contain runtime dependencies and a
`licenses/` directory. `--webgpu no` builds the original renderer set.

The existing Emscripten pin remains in force. Browser linking uses a writable
cache for the external Dawn port; set `TwoDogWebGpuCachePath` to override its
default under the system temporary directory. Keep it separate from a native
SCons cache, and on the Emscripten SDK's drive on Windows.

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
Editor builds and publishes copy GodotTools and its dependencies into
`GodotSharp/Tools` beside the host executable. Custom hosts must keep pumping
the engine and avoid assuming that a game scene is running in editor mode.

Resource import also runs separately and automatically at build time; see
[Resource Import](./import-tool).
:::

