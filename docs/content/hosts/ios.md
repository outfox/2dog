---
title: iOS
description: "Run your Godot C# game as an iOS app with the experimental iOS host, built on a Mac with Xcode."
tagline: "Learn how to put your Godot C# game on an iPhone or iPad."
---

# iOS

The iOS host is a .NET for iOS app that links the Godot engine statically and
shows Godot's own view controller. Its display link owns the frame loop, input
and lifecycle; the host registers the .NET runtime before Godot starts.

::: warning Experimental
Only `ios-arm64` (devices) and `iossimulator-arm64` (the Simulator on Apple
silicon Macs) are supported. Building needs a Mac with Xcode. Intel Mac
Simulators and Mac Catalyst are unsupported; physical-device validation is
still needed.
:::

## Set It Up

```bash
dnx 2dog add --ios
```

This creates `MyGame.ios/` next to `project.godot`, with the project file, an
`AppDelegate`, a `SceneDelegate` that starts Godot through `GodotHost.Start`
when the window scene connects, an `Info.plist`, a launch screen and an app
icon. It also adds an `iOS` export preset. Set
`ApplicationId` in the project file to your own bundle identifier.

You also need the .NET `ios` workload and Xcode with the iOS platform
installed. Only a Mac can build iOS apps: .NET for iOS links with Xcode's
clang and iOS SDK, signs with `codesign`, and its Mono AOT compiler runs on
macOS only. From Windows, use Pair to Mac; Linux cannot build iOS apps.
`2dog doctor` reports the workload and Xcode.

Run workload and build commands inside the generated host directory so its
`global.json` selects the .NET 10 SDK. Install an iOS workload compatible with
your installed Xcode; the workload and Xcode SDK must match.

```bash
cd MyGame.ios
dotnet workload install ios
dnx 2dog doctor
```

The host is excluded from ordinary solution builds. Build it directly using
the commands below.

## Build and Run

Start an iPhone or iPad in Xcode's Simulator first. From `MyGame.ios/`, build
and install the app; Simulator builds need no signing identity:

```bash
dotnet build -c Debug -r iossimulator-arm64
xcrun simctl install booted bin/Debug/net10.0-ios/iossimulator-arm64/MyGame.ios.app
xcrun simctl launch --console booted com.companyname.mygame
```

Use the `ApplicationId` from your project file in the launch command.

On a device, with your signing identity and provisioning profile:

```bash
dotnet publish -r ios-arm64 -c Release -p:CodesignKey="Apple Development" -p:CodesignProvision="MyGame Dev"
```

Building imports the game, exports its pack with the `iOS` preset, bundles it as
`game.pck` and force-loads the engine archive into the app. Debug builds run the
Mono interpreter, Release builds are AOT-compiled.

A Simulator booted outside a GUI session (over SSH, on CI) has no audio service
and CoreAudio aborts the app when the engine starts. Pass `--audio-driver Dummy`
as an engine argument to `GodotHost.Start` there; devices need nothing.

See [iOS Configuration](/configuration/ios) for build settings.

## Try a CI Build

To try the showcase without installing .NET or building the engine, use an
Apple silicon Mac with Xcode and an installed iOS Simulator runtime:

1. Open a successful [CI run](https://github.com/outfox/2dog/actions/workflows/ci.yml)
   whose **iOS Smoke** jobs ran.
2. Download the `ios-simulator-showcase-double-Release` artifact from the run's
   **Artifacts** section. GitHub requires you to sign in to download artifacts;
   they are retained for seven days.
3. Extract the downloaded artifact, then run these commands in the extracted
   directory with a Simulator already booted:

```bash
ditto -x -k simulator-app.zip ios-showcase
xcrun simctl install booted ios-showcase/showcase.ios.app
xcrun simctl launch --console booted dev.twodog.showcase
```

The inner ZIP preserves the app bundle's permissions and layout. These are
Simulator apps; they cannot be installed on an iPhone or iPad. Build and sign
your own device app using the command above.

CI builds, inspects and launches three complementary Simulator apps:

| Artifact | Coverage |
| --- | --- |
| `ios-simulator-smoke-single-Debug` | Basic C# game startup with the interpreter |
| `ios-simulator-showcase-double-Release` | Trimmed, AOT-compiled showcase with double precision and a static GDExtension |
| `ios-simulator-maui-single-Debug` | Godot embedded in a MAUI page |

Each job requires its C# success marker in the app's console. Its matching
`ios-simulator-log-...` artifact contains the Simulator log. This coverage does
not validate physical-device input, audio or background handling.

## Host Code

Do not call `Engine.Start()`: Godot's view controller runs the loop. The hosts
use the UIKit scene lifecycle: start the engine when the window scene connects
and reach the scene once it runs:

```csharp
// SceneDelegate.WillConnect
Window ??= new UIWindow(windowScene)
{
    RootViewController = GodotHost.ViewController
        ?? GodotHost.Start("MyGame", TwoDogWebBoot.PluginsInitializer()),
};
GodotHost.Started += () => GodotHost.Dispatch(tree => tree.Root.PrintTree());
```

Godot reads the interface orientation from the app delegate's window, so the
scene delegate assigns its window there too.

`GodotHost.Start` registers the runtime with `IosHost.Register`, creates the
engine and returns the view controller to present or embed. `Started` and
`QuitRequested` run on the main thread, which Godot shares with UIKit;
`Dispatch` runs between frames. One engine runs per process.

## Rendering

Devices use Godot's Metal driver; the Simulator has no Metal driver in Godot
and runs the Compatibility renderer on GLES3.

## GDExtensions

iOS links extensions into the app. List a static library, or an `.xcframework`
of static libraries with device and Simulator slices, under the `ios.arm64` key
of the `.gdextension` file; `[dependencies]` entries link the same way. The
build force-loads the slice for the `RuntimeIdentifier` and keeps the entry
symbol, which Godot resolves from the app itself. Every listed file must exist,
because the pack export copies it. Opt out with `TwoDogIosGdExtensions=false`.

## Limitations

- Native AOT (`PublishAot`) is not supported; builds use Mono.
- Device validation, input and background handling are not validated yet.

To embed the game in a MAUI page instead, use the [MAUI host](./maui).
