---
title: MSBuild Configuration
description: "MSBuild properties and package versions for desktop and Web hosts."
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

[`2dog update`](/cli/update) rewrites that block (and the game
project's `Godot.NET.Sdk` version, which cannot come from a property);
[`2dog doctor`](/cli/doctor) reports literals left in host csprojs and versions on
different Godot lines.

`2dog.engine` selects the platform meta package for the current OS:
`2dog.win-x64`, `2dog.linux-x64`, or `2dog.osx-arm64`. Each meta package pins
the `release`, `debug`, and `editor` native packages. The selected native is
copied as `libgodot-<variant>.dll`, `.so`, or `.dylib` and loaded by that name.

For xUnit, reference `2dog.xunit`; it brings in `2dog.engine`. Web hosts
also reference `2dog.browser-wasm`.

## Web Host

Set these optional properties in the Web host's `.csproj`, or the Blazor
client's `.csproj`:

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogWebVariant` | `release`, or `debug` in Debug when the debug package is restored | Engine build; `debug` needs a `2dog.browser-wasm.debug` reference |
| `TwoDogExportPack` | `true` | Export content; `false` uses `wwwroot/godot.pck` |
| `TwoDogWebExportPreset` | `Web` | Preset in `export_presets.cfg` |
| `TwoDogWebPackName` | `godot.pck` | Deployed pack name |
| `TwoDogWebSizeManifest` | `true` | Write `twodog.sizes.json` for loading progress |
| `TwoDogWebStripMaps` | `true` for release | Remove JavaScript source maps |
| `TwoDogWebPrecompress` | `true` with Core MSBuild | Write Brotli and gzip copies of large files; skipped with full-framework MSBuild |
| `TwoDogWebPrecompressLevel` | `Optimal` | Compression level; `SmallestSize` takes longer |
| `TwoDogWebSideModuleExports` | `true` | Export symbols used by GDExtensions |
| `WasmEmitSymbolMap` | `false` | Include native symbols for stack traces |
| `WasmInitialHeapSize` | `256MB` | Initial memory allocation; memory can grow |

Blazor manages its own bundle, progress, and compression, so size-manifest,
source-map stripping, and precompression settings apply only to Web and WebXR.

For libraries accessed only through reflection, add a trimmer root:

```xml
<TrimmerRootAssembly Include="MyLibrary"/>
```

The generated host already preserves the game, host, `GodotSharp`, and
`twodog` assemblies.
