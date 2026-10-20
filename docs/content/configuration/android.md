---
title: Android Configuration
description: "MSBuild configuration for experimental Android hosts: native variants, game packs, signing, trimming, and Native AOT."
tagline: "Learn how to configure, sign and slim down your Android builds."
---

# Android Configuration

Set properties in `MyGame.android.csproj`. The generated host references
`2dog.engine`, `2dog.android`, and the native meta package for its selected
`android-arm64` or `android-x64` RID.
See [global settings](/configuration#properties) for the game directory and import controls.

## Properties

| Property                    | Default                                            | Purpose                                                                               |
|-----------------------------|----------------------------------------------------|---------------------------------------------------------------------------------------|
| `TwoDogVariant`             | Follows the [configuration](/build-configurations) | Native engine variant: `debug` or `release`                                           |
| `TwoDogAndroidGameAssembly` | Set by the generated host                          | Game assembly that Godot loads; kept whole by the trimmer                             |
| `TwoDogAndroidExportPreset` | `Android`                                          | Preset in `export_presets.cfg`                                                        |
| `TwoDogAndroidPack`         | Exported by the build                              | Use an existing game pack instead of exporting one                                    |
| `TwoDogAndroidGdExtensions` | `true`                                             | Package the Android libraries of the project's GDExtensions                           |
| `TwoDogAndroidSigning`      | `true`                                             | Sign using .NET signing properties, Godot's keystore variables, or the debug keystore |
| `AndroidPackageFormats`     | `apk` in the generated host                        | `apk` for sideloading, `aab` for app stores                                           |

Android exports its pack during the build and puts it inside the APK/AAB;
`TwoDogExportPack` does not apply.
See the [Android host guide](/hosts/android#build-and-install) for SDK setup
and installation.

## Signing

Publishing signs with the first keystore it finds:

1. The .NET `AndroidKeyStore` and `AndroidSigning*` properties
2. Godot's `GODOT_ANDROID_KEYSTORE_RELEASE_PATH`, `_USER`, and `_PASSWORD`
   [environment variables](/configuration/environment-variables#net-and-android-tooling)
   (`DEBUG` variables for Debug builds); set all three together
3. Android's debug keystore, `~/.android/debug.keystore`
4. The .NET debug keystore

## Trimming and Mono AOT

Release builds use .NET for Android's default trimming and AOT; see its
[build properties](https://learn.microsoft.com/dotnet/android/building-apps/build-properties).
2dog preserves `GodotSharp`, `GodotPlugins`, `twodog`, and the game assembly.
Root other assemblies accessed only through reflection:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MyLibrary" RootMode="All"/>
</ItemGroup>
```

## Native AOT

.NET for Android's Native AOT is experimental. It supports 2dog's debug and
release variants and needs the Android NDK:

```bash
dotnet publish MyGame.android -c Release -p:PublishAot=true -p:AndroidNdkDirectory=$ANDROID_HOME/ndk/29.0.14206865
```

Without `PublishAot`, Release builds use Mono's AOT compiler through
`RunAOTCompilation`.
