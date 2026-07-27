---
title: Build Variants
description: "Choosing between the debug, release, and editor native Godot variants in 2dog, and how the Debug, Release, and Editor .NET configurations map onto them."
---

# Build Variants

2dog offers three native Godot variants. Pick the lightest one that has the
features your host needs.

| .NET configuration | Recommended variant | Godot build | `TOOLS_ENABLED` | Use |
| --- | --- | --- | --- | --- |
| **Debug** | `debug` | `template_debug` | No | Development, debugging, tests |
| **Release** | `release` | `template_release` | No | Production and final validation |
| **Editor** | `editor` | `editor` | Yes | Editor types and `[Tool]` scripts |

## Debug Configuration

The `debug` variant includes assertions and debugging support, but not editor
features.

```bash
dotnet build -c Debug
dotnet run -c Debug
dotnet test -c Debug
```

## Release Configuration

The `release` variant is the optimized production runtime and the default when
`TwoDogVariant` is unset.

```bash
dotnet build -c Release
dotnet run -c Release
dotnet publish -c Release
```

## Editor Configuration

The `editor` variant enables `TOOLS_ENABLED`, including editor types, import
plugins, and `[Tool]` scripts.

```bash
dotnet build -c Editor
dotnet run -c Editor
dotnet test -c Editor
```

::: warning Editor runtime limitations
The variant provides compile-time access to editor APIs, but embedded libgodot
does not initialize editor runtime singletons such as `EditorInterface`.
Resource import runs separately and automatically at build time; see
[Resource Import](./import-tool).
:::

## Selecting a Variant

The .NET configuration does not select the native variant automatically.
`2dog.engine` defaults to `release`; map Debug and Editor explicitly with
`TwoDogVariant`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <TwoDogVariant Condition="'$(Configuration)' == 'Debug'">debug</TwoDogVariant>
    <TwoDogVariant Condition="'$(Configuration)' == 'Editor'">editor</TwoDogVariant>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog.engine" Version=":2dog-version:"/>
  </ItemGroup>
</Project>
```

All three desktop variants come through the platform meta package, so this
needs no extra package references. `TwoDogVariant` also controls the copied
native library, GodotPlugins layout, and embedded runtime metadata.

Use the configuration in the usual way:

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
