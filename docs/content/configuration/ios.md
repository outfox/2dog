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
| `UseInterpreter` | `true` in Debug | Run the Mono interpreter so no code path needs a JIT; Release builds AOT-compile |
| `RuntimeIdentifier` | `ios-arm64` in the generated host | `iossimulator-arm64` for the Simulator on Apple silicon |
| `CodesignKey`, `CodesignProvision` | Unset | Signing identity and provisioning profile for device builds |

iOS exports its pack during the build and bundles it as `game.pck`.
`TwoDogExportPack` is disabled by the iOS targets because the desktop publish
export is not used; supply an existing pack through `TwoDogIosPack`. The
targets also set `TwoDogSkipGodotApiCopy`: iOS has no `GodotPlugins.dll`, the
game's generated initializer registers its scripts through `IosHost`.

The engine archive is linked with `ForceLoad` and the Swift runtime, since
Godot registers classes from static initializers and carries a SwiftUI
wrapper. Both native variants restore; a missing selected payload fails the
build (`TDGI003`). Other RIDs fail with `TDGI005`.

## Trimming and AOT

Release builds are trimmed and AOT-compiled, like any .NET for iOS app. 2dog
preserves `GodotSharp`, `twodog`, `twodog.ios`, and the game assembly. Root
other assemblies accessed only through reflection:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="MyLibrary"/>
</ItemGroup>
```

Native AOT (`PublishAot`) is rejected with `TDGI004`; iOS hosts run on Mono.

See the [iOS host guide](/hosts/ios) for building, the Simulator and signing.
