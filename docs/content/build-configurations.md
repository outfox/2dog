---
title: Build Variants
description: "Choosing between the debug, release, and editor native Godot variants in 2dog, and how the Debug, Release, and Editor .NET configurations map onto them."
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

::: warning Editor runtime limitations
The variant provides compile-time access to editor APIs, but embedded libgodot
does not initialize editor runtime singletons such as `EditorInterface`.
Resource import runs separately and automatically at build time; see
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
