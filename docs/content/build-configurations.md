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
defaults stay unchanged. Double precision, threaded web, Android, and WebXR
retain their existing renderer paths.

Select WebGPU Mobile in a desktop host with Godot's arguments:

```bash
dotnet run --project MyGame.2dog -- --rendering-driver webgpu --rendering-method mobile
```

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
its pinned CMake through `uv`. An existing SDK can be selected with
`--webgpu-native-path <sdk>`; it must contain runtime dependencies and a
`licenses/` directory. `--webgpu no` builds the original renderer set.

The existing Emscripten pin remains in force. Browser linking uses a writable
cache for the external Dawn port; set `TwoDogWebGpuCachePath` to override its
default under the system temporary directory. Keep it separate from a native
SCons cache, and on the Emscripten SDK's drive on Windows.

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

