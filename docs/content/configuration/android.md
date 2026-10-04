---
title: Android Configuration
description: "MSBuild configuration for experimental Android hosts: native variants, game packs, signing, trimming, and Native AOT."
---

# Android Configuration

Set properties in `MyGame.android.csproj`. The generated host references
`2dog.engine`, `2dog.android`, and the native meta package for its selected
`android-arm64` or `android-x64` RID.
See [global settings](/configuration#properties) for the game directory and import controls.

## Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogVariant` | `debug` in Debug, `editor` in Editor, `release` otherwise | Native engine variant; explicit settings override the default. The `editor` variant is unsupported |
| `TwoDogAndroidGameAssembly` | Set by the generated host | Game assembly that Godot loads; kept whole by the trimmer |
| `TwoDogAndroidExportPreset` | `Android` | Preset in `export_presets.cfg` |
| `TwoDogAndroidPack` | Exported by the build | Use an existing game pack instead of exporting one |
| `TwoDogAndroidGdExtensions` | `true` | Package the Android libraries of the project's GDExtensions |
| `TwoDogAndroidSigning` | `true` | Sign using .NET signing properties, Godot's keystore variables, or the debug keystore |
| `AndroidPackageFormats` | `apk` in the generated host | `apk` for sideloading, `aab` for app stores; .NET for Android otherwise defaults to `aab` in Release |

Android exports its pack during the build and puts it inside the APK/AAB.
`TwoDogExportPack` is disabled by the Android targets because the desktop
publish export is not used; supply an existing pack through `TwoDogAndroidPack`.
See the [Android host guide](/hosts/android#build-and-install) for SDK setup,
installation, and keystore precedence.

## Trimming and Mono AOT

Release builds are trimmed and AOT-compiled by default (`TrimMode=partial`,
profiled AOT). 2dog preserves `GodotSharp`, `GodotPlugins`, `twodog`, and the
game assembly. `TrimMode=full` and `AndroidEnableProfiledAot=false` (AOT for
every method) also work. Root other assemblies accessed only through reflection:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MyLibrary" RootMode="All"/>
</ItemGroup>
```

## Native AOT

.NET for Android's Native AOT is experimental (warning XA1040). It supports
2dog's debug and release variants, links with the Android NDK, and can build
on any supported build machine. Install the NDK and set `AndroidNdkDirectory`:

```bash
dotnet publish MyGame.android -c Release -p:PublishAot=true -p:AndroidNdkDirectory=$ANDROID_HOME/ndk/29.0.14206865
```

Without `PublishAot`, Release builds use Mono's AOT compiler through
`RunAOTCompilation`.
