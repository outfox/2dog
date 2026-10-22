---
title: iOS Configuration
description: "MSBuild configuration for experimental iOS hosts: native variants, game packs, signing, trimming, and the interpreter."
tagline: "Learn how to configure, sign and slim down your iOS builds."
---

# iOS Configuration

Set properties in `MyGame.ios.csproj`. The generated host references
`2dog.engine`, `2dog.ios`, and the native meta package for its selected
`ios-arm64` or `iossimulator-arm64` RID.
See [global settings](/configuration#properties) for the game directory and import controls.

## Properties

| Property | Default | Purpose |
| --- | --- | --- |
| `TwoDogVariant` | `debug` in Debug, `editor` in Editor, `release` otherwise | Native engine variant; explicit settings override the default. The `editor` variant is unsupported |
| `TwoDogIosGameAssembly` | Set by the generated host | Game assembly kept whole by the trimmer |
| `TwoDogIosExportPreset` | `iOS` | Preset in `export_presets.cfg` |
| `TwoDogIosPack` | Exported by the build | Use an existing game pack instead of exporting one |
| `TwoDogIosGdExtensions` | `true` | Link the static libraries or xcframework slices the project's `.gdextension` files list for iOS |
| `UseInterpreter` | `true` in Debug | Run the Mono interpreter so no code path needs a JIT; Release builds AOT-compile |
| `RuntimeIdentifier` | `ios-arm64` in the generated host | `iossimulator-arm64` for the Simulator on Apple silicon |
| `SupportedOSPlatformVersion` | `15.0` in the generated host | Minimum iOS version; the Simulator runtime must also meet the installed workload's requirements |
| `CodesignKey`, `CodesignProvision` | Unset | Signing identity and provisioning profile for device builds |

iOS exports its pack during the build and bundles it as `game.pck`.
The iOS targets disable `TwoDogExportPack`, because iOS does not use the
desktop publish export. To use an existing pack, set `TwoDogIosPack`. The
targets also set `TwoDogSkipGodotApiCopy`, because iOS has no
`GodotPlugins.dll`. Instead, the game's generated initializer registers its
scripts through `IosHost`.

The build links the engine archive with `ForceLoad` and the Swift runtime,
because Godot registers classes from static initializers and carries a SwiftUI
wrapper. Both native variants restore. If the payload for the selected variant is
missing, the build fails with `TDGI003`. Other RIDs fail with `TDGI005`.

## Trimming and AOT

Release builds are trimmed and AOT-compiled, like any .NET for iOS app. 2dog
preserves `GodotSharp`, `twodog`, `twodog.ios`, and the game assembly. If your
code uses another assembly only through reflection, add it as a trimmer root:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MyLibrary"/>
</ItemGroup>
```

The build rejects Native AOT (`PublishAot`) with `TDGI004`, because iOS hosts
run on Mono.

See the [iOS host guide](/hosts/ios) for building, the Simulator and signing.
