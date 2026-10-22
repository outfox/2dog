---
title: MSBuild Configuration
description: "Shared 2dog settings, package versions, and configuration references for each host."
tagline: "Learn how to tune 2dog's MSBuild settings for every host."
---

# MSBuild Configuration

Set host properties inside a `<PropertyGroup>` in the host's `.csproj`. Set
shared settings in the root
[`Directory.Build.props`](https://learn.microsoft.com/visualstudio/msbuild/customize-by-directory).

## Global Properties {#properties}

| Property | Default | Purpose |
| --- | --- | --- |
| `GodotProjectDir` | None | Directory containing `project.godot`; enables resource import and is embedded for `Engine.ResolveProjectDir()` |
| `TwoDogVariant` | `debug` in Debug, `editor` in Editor, `release` otherwise | Desktop/Android/iOS native variant; explicit settings override the default. Android and iOS do not support `editor`; browser hosts use `TwoDogWebVariant` |
| `TwoDogRemoveDuplicateGodotAnalyzers` | `false` | Removes duplicate analyzers from a host that also references a `Godot.NET.Sdk` game project |

For a host nested directly inside the Godot project:

```xml
<PropertyGroup>
  <GodotProjectDir>..</GodotProjectDir>
  <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
</PropertyGroup>
```

The build resolves `GodotProjectDir` relative to the project file and embeds
it as an absolute path. The game project keeps its Godot source generator; only the
host should remove duplicate analyzers. Blazor clients are nested an extra
level and normally use `../..`.

See [Build Variants](./build-configurations) for native selection and
[Resource Import](./import-tool) for import controls, including
`TwoDogAutoImport`, `TwoDogRequireImport`, and `GodotEditor`.

## Packages and Versions

Package versions begin with the embedded Godot version. Reference exact
versions. Projects that 2dog sets up keep them in one block of the root
`Directory.Build.props`:

```xml
<PropertyGroup Label="2dog">
  <TwoDogVersion>:2dog-version:</TwoDogVersion>
  <TwoDogNativesVersion>:natives-version:</TwoDogNativesVersion>
  <TwoDogGodotVersion>:godot-version:</TwoDogGodotVersion>
  <!-- TwoDogAvaloniaVersion, TwoDogWindowsAppSdkVersion, TwoDogAspNetCoreVersion, TwoDogMauiVersion,
       TwoDogQtBridgeVersion -->
</PropertyGroup>
```

Hosts reference those properties:

```xml
<PackageReference Include="2dog.engine" Version="$(TwoDogVersion)"/>
<PackageReference Include="2dog.browser-wasm" Version="[$(TwoDogNativesVersion)]"/>
```

`TwoDogAspNetCoreVersion` is a floating version on the patch level
(`major.minor.*`). Each restore of a Blazor host takes the newest ASP.NET Core
servicing release and its security fixes.

[`2dog update`](/cli/update) rewrites the version block and the game project's
`Godot.NET.Sdk` version, which cannot come from a property.
[`2dog doctor`](/cli/doctor) reports remaining literal versions and mismatched
Godot lines.

`2dog.engine` references the desktop meta packages `2dog.win-x64`,
`2dog.linux-x64`, and `2dog.osx-arm64`. Their targets select the native
library for the build or publish platform. Each pins the release, debug, and
editor packages. The build copies the selected library as
`libgodot-<variant>.dll`, `.so`, or `.dylib`.
`2dog.browser-wasm` pins both release and debug browser natives.

## Desktop Publishing

These properties apply to the generic, Avalonia, WinForms and WinUI hosts:

| Property                    | Default                   | Purpose                                                                                                         |
|-----------------------------|---------------------------|-----------------------------------------------------------------------------------------------------------------|
| `TwoDogExportPack`          | `true`                    | Export game content to a `.pck` next to the executable; `false` skips export, so supply game content separately |
| `TwoDogDesktopExportPreset` | RID-mapped                | Preset in `export_presets.cfg`: `Windows Desktop`, `Linux`, or `macOS`, according to the publish target         |
| `PublishSelfContained`      | `true` in generated hosts | Bundle the .NET runtime; `false` requires an installed runtime                                                  |
| `PublishAot`                | `false`                   | Compile the host and managed game code into a [native executable](#native-aot)                                  |
| `PublishReadyToRun`         | `false`                   | Precompile the engine and host assemblies for [faster startup](#readytorun)                                     |

`PublishSingleFile` is unsupported; Native AOT produces one executable instead.
Generated desktop hosts build as `WinExe`, which hides the Windows console.
The generic host does this only in Release. To keep console output, set
`OutputType=Exe`. Linux and macOS treat `WinExe` as `Exe`.

## Native AOT

Generic, Avalonia, and WinUI hosts support `PublishAot=true` with debug or
release natives. The editor variant loads GodotTools at runtime and cannot
use Native AOT. WinForms does not support trimming or Native AOT.

Desktop Native AOT requires the platform's native toolchain:

- Windows: Visual Studio C++ build tools
- Linux: `clang` and `zlib1g-dev`
- macOS: Xcode command-line tools

Publish on the operating system that you target. See the
[.NET prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites).

Native AOT trims the app. See [Trimming](#trimming) for assemblies that your
code uses only through reflection. Android has [experimental Native AOT](./configuration/android#native-aot)
with NDK requirements and different trimmer roots. Browser hosts use Mono's
AOT compiler through `RunAOTCompilation`, described in their host references.

## ReadyToRun

Generic, Avalonia, WinForms, and WinUI hosts support
[ReadyToRun](https://learn.microsoft.com/dotnet/core/deploying/ready-to-run)
with a runtime identifier:

```bash
dotnet publish MyGame.2dog -c Release -r win-x64 -p:PublishReadyToRun=true
```

It precompiles the host assembly and the engine assemblies `GodotSharp` and
`twodog`. The JIT still compiles game scripts, because Godot loads the game
assembly from a stream.

Browser and Android hosts run on Mono, which has no ReadyToRun; use
`RunAOTCompilation` there.

## Trimming

Native AOT, browser, Blazor and Android publishes are trimmed. 2dog keeps
`GodotSharp`, `twodog`, the host and your game assembly whole. If your code
uses another assembly only through reflection, add it as a trimmer root:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MyLibrary"/>
</ItemGroup>
```
