---
title: MSBuild Configuration
description: "2dog MSBuild properties for host projects: GodotProjectDir, TwoDogVariant, duplicate Godot analyzer removal, package versioning, and the GodotSharp directory."
---

# Configuration

Set 2dog properties in your host project's `.csproj`.

## Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `GodotProjectDir` | None | Directory containing `project.godot`; enables automatic resource import and is embedded for `Engine.ResolveProjectDir()` |
| `TwoDogVariant` | `release` | Native desktop variant: `release`, `debug`, or `editor` |
| `TwoDogRemoveDuplicateGodotAnalyzers` | `false` | Removes duplicate analyzers from a host that also references a `Godot.NET.Sdk` game project |
| `TwoDogExportPack` | `true` | Desktop publishes export the game content as an exe-adjacent `.pck`; `false` skips it (also disables the web host's pack export) |
| `TwoDogDesktopExportPreset` | RID-mapped | Export preset for the desktop pack; defaults to `Windows Desktop`, `Linux`, or `macOS` by publish target |

The standard nested-host setup is:

```xml
<PropertyGroup>
  <GodotProjectDir>..</GodotProjectDir>
  <TwoDogVariant Condition="'$(Configuration)' == 'Debug'">debug</TwoDogVariant>
  <TwoDogVariant Condition="'$(Configuration)' == 'Editor'">editor</TwoDogVariant>
  <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
</PropertyGroup>
```

`GodotProjectDir` is resolved relative to the project file and embedded as an
absolute path. The game project itself must keep its Godot source generator;
only the host should remove duplicate analyzers.

See [Selecting a Variant](./build-configurations#selecting-a-variant) for the
variant mapping and [Resource Import](./import-tool) for import properties.

## Packages and Versions

Reference `2dog.engine` from generic hosts:

```xml
<PackageReference Include="2dog.engine" Version=":godot-version:.*"/>
```

Package versions begin with the embedded Godot version. Pin manual references
to your project's Godot line, as above, so NuGet does not silently select a
newer engine line. Projects scaffolded by 2dog keep every version in one block
of the root `Directory.Build.props` and reference it from the hosts:

```xml
<PropertyGroup Label="2dog">
  <TwoDogVersion>:2dog-version:</TwoDogVersion>
  <TwoDogNativesVersion>:natives-version:</TwoDogNativesVersion>
  <TwoDogGodotVersion>:godot-version:</TwoDogGodotVersion>
  <!-- TwoDogAvaloniaVersion, TwoDogWindowsAppSdkVersion, TwoDogAspNetCoreVersion -->
</PropertyGroup>
```

```xml
<PackageReference Include="2dog.engine" Version="$(TwoDogVersion)"/>
<PackageReference Include="2dog.browser-wasm" Version="[$(TwoDogNativesVersion)]"/>
```

[`2dog update`](/doctor#updating-a-project) rewrites that block (and the game
project's `Godot.NET.Sdk` version, which cannot come from a property);
[`2dog doctor`](/doctor) reports literals left in host csprojs and versions on
different Godot lines.

`2dog.engine` selects the platform meta package for the current OS:
`2dog.win-x64`, `2dog.linux-x64`, or `2dog.osx-arm64`. Each meta package pins
the `release`, `debug`, and `editor` native packages. The selected native is
copied as `libgodot-<variant>.dll`, `.so`, or `.dylib` and loaded by that name.

For xUnit, reference `2dog.xunit`; it brings in `2dog.engine`. Browser hosts
also reference `2dog.browser-wasm`; see [Browser Host](/hosts/web#configuration)
for web-specific properties and packaging.
