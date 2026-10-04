---
title: MSBuild Configuration
description: "Shared 2dog settings, MSBuild runtimes, package versions, and configuration references for each host."
---

# MSBuild Configuration

Set host properties inside a `<PropertyGroup>` in its `.csproj`. Put shared
settings in `Directory.Build.props`. Project settings override shared values;
command-line properties (`-p:Name=value`) override project settings. Omitted
2dog properties use their documented defaults.

## Global Properties {#properties}

| Property | Default | Purpose |
| --- | --- | --- |
| `GodotProjectDir` | None | Directory containing `project.godot`; enables resource import and is embedded for `Engine.ResolveProjectDir()` |
| `TwoDogVariant` | `debug` in Debug, `editor` in Editor, `release` otherwise | Desktop/Android native variant; explicit settings override the default. Android does not support `editor`; browser hosts use `TwoDogWebVariant` |
| `TwoDogRemoveDuplicateGodotAnalyzers` | `false` | Removes duplicate analyzers from a host that also references a `Godot.NET.Sdk` game project |

For a host nested directly inside the Godot project:

```xml
<PropertyGroup>
  <GodotProjectDir>..</GodotProjectDir>
  <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
</PropertyGroup>
```

`GodotProjectDir` is resolved relative to the project file and embedded as an
absolute path. The game project keeps its Godot source generator; only the
host should remove duplicate analyzers. Blazor clients are nested an extra
level and normally use `../..`.

See [Build Variants](./build-configurations) for native selection and
[Resource Import](./import-tool) for import controls, including
`TwoDogAutoImport`, `TwoDogRequireImport`, and `GodotEditor`.

## Packages and Versions

Package versions begin with the embedded Godot version. Pin manual references
to your project's Godot line, for example `Version=":godot-version:.*"`, so
NuGet does not silently select a newer engine line. Scaffolded projects keep
versions in one block of the root `Directory.Build.props`:

```xml
<PropertyGroup Label="2dog">
  <TwoDogVersion>:2dog-version:</TwoDogVersion>
  <TwoDogNativesVersion>:natives-version:</TwoDogNativesVersion>
  <TwoDogGodotVersion>:godot-version:</TwoDogGodotVersion>
  <!-- TwoDogAvaloniaVersion, TwoDogWindowsAppSdkVersion, TwoDogAspNetCoreVersion -->
</PropertyGroup>
```

Hosts reference those properties:

```xml
<PackageReference Include="2dog.engine" Version="$(TwoDogVersion)"/>
<PackageReference Include="2dog.browser-wasm" Version="[$(TwoDogNativesVersion)]"/>
```

[`2dog update`](/cli/update) rewrites the version block and the game project's
`Godot.NET.Sdk` version, which cannot come from a property.
[`2dog doctor`](/cli/doctor) reports remaining literal versions and mismatched
Godot lines.

`2dog.engine` references the desktop meta packages `2dog.win-x64`,
`2dog.linux-x64`, and `2dog.osx-arm64`. Their targets select the native for the
build or publish platform. Each pins the release, debug, and editor packages;
the selected library is copied as `libgodot-<variant>.dll`, `.so`, or `.dylib`.
`2dog.browser-wasm` pins both release and debug browser natives.

## Native AOT

Generic, Avalonia, and WinUI hosts support `PublishAot=true` with debug or
release natives. The editor variant loads GodotTools at runtime and cannot
use Native AOT. WinForms does not support trimming or Native AOT.

Desktop Native AOT requires the platform's native toolchain: Visual Studio
C++ build tools on Windows, `clang` and `zlib1g-dev` on Linux, or Xcode
command-line tools on macOS. Publish on the operating system you target; see
the [.NET prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites).

2dog preserves `GodotSharp` and the game assembly for reflection. Root other
assemblies that are only accessed through reflection:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MyLibrary"/>
</ItemGroup>
```

Android has [experimental Native AOT](./configuration/android#native-aot)
with NDK requirements and different trimmer roots. Browser hosts use Mono's
AOT compiler through `RunAOTCompilation`, described in their host references.

## MSBuild Runtimes

`Core`, `Full`, and `Mono` describe the runtime executing MSBuild. They are
independent of your project's `TargetFramework`, build `Configuration`, and
the runtime used by your published app. Windows builds can use Core or Full.

| `MSBuildRuntimeType` | Build tool |
| --- | --- |
| `Core` | MSBuild from the .NET SDK: `dotnet build`, `dotnet publish`, or `dotnet msbuild` |
| `Full` | .NET Framework MSBuild, such as Visual Studio's `MSBuild.exe` |
| `Mono` | Legacy MSBuild running on Mono |

MSBuild sets this [reserved property](https://learn.microsoft.com/visualstudio/msbuild/msbuild-reserved-and-well-known-properties#reserved-and-well-known-properties)
automatically. To inspect the runtime used by a command:

```bash
dotnet msbuild MyGame.2dog/MyGame.2dog.csproj -getProperty:MSBuildRuntimeType
```

Use the .NET 10+ SDK for 2dog's CLI workflows. Currently, Web and WebXR
[precompression](./configuration/web#precompression) runs only under Core
MSBuild; Full and Mono skip it even when explicitly enabled. The current
inline task depends on Core's compression APIs.
