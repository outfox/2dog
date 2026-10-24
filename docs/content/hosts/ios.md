---
title: iOS
description: "Run your Godot C# game as an iOS app with the experimental iOS host, built on a Mac with Xcode."
tagline: "Learn how to put your Godot C# game on an iPhone or iPad."
keywords: "Godot C# iOS, .NET for iOS"
---

# iOS

The iOS host is a .NET for iOS app that links the Godot engine statically and
shows Godot's own view controller. The display link of this view controller owns
the frame loop, input and lifecycle. The host registers the .NET runtime before
Godot starts.

::: warning Experimental
The host supports only `ios-arm64` (devices) and `iossimulator-arm64` (the
Simulator on Apple silicon Macs). You must build it on a Mac with Xcode. The
host does not support Intel Mac Simulators or Mac Catalyst. It is not tested
on physical devices yet.
:::

## Set It Up

```bash
dnx 2dog add --ios
```

This creates `MyGame.ios/` next to `project.godot`. The folder contains:

- the project file
- an `AppDelegate`
- a `SceneDelegate` that starts Godot through `GodotHost.Start` when the
  window scene connects
- an `Info.plist`
- a launch screen and an app icon

It also adds an `iOS` export preset. Set `ApplicationId` in the project file
to your own bundle identifier.

You also need the .NET `ios` workload and Xcode with the iOS platform
installed. Only a Mac can build iOS apps, because .NET for iOS uses Xcode's
clang and iOS SDK to link and `codesign` to sign. Its Mono AOT compiler also
runs only on macOS. On Windows, use Pair to Mac. Linux cannot build iOS apps.
`2dog doctor` reports the workload and Xcode.

Run workload and build commands inside the generated host directory so its
`global.json` selects the .NET 10 SDK. Install an iOS workload compatible with
your installed Xcode; the workload and Xcode SDK must match.

```bash
cd MyGame.ios
dotnet workload install ios
dnx 2dog doctor
```

Ordinary solution builds skip this host. Build it directly with the commands
below.

## Build and Run

Start an iPhone or iPad in Xcode's Simulator first. Simulator builds do not
need a signing identity. From `MyGame.ios/`, run these commands to build,
install and launch the app:

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

The build imports the game, exports its pack with the `iOS` preset, bundles it
as `game.pck` and force-loads the engine archive into the app. Debug builds run
the Mono interpreter. Release builds are AOT-compiled.

If you boot the Simulator outside a GUI session (over SSH or on CI), pass
`--audio-driver Dummy` as an engine argument to `GodotHost.Start`. Without a
GUI session, the Simulator has no audio service, and CoreAudio aborts the app
when the engine starts. Devices do not need this argument.

See [iOS Configuration](/configuration/ios) for build settings.

## Try a CI Build

To try the showcase without installing .NET or building the engine, use an
Apple silicon Mac with Xcode and an installed iOS Simulator runtime:

1. Open a successful [CI run](https://github.com/outfox/2dog/actions/workflows/ci.yml)
   whose **iOS Smoke** jobs ran.
2. Download the `ios-simulator-showcase-double-Release` artifact from the run's
   **Artifacts** section. You must sign in to GitHub to download artifacts.
   GitHub keeps artifacts for seven days.
3. Extract the downloaded artifact.
4. Start a Simulator.
5. Run these commands in the extracted directory:

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

A job passes only when its C# success marker appears in the app's console.
The matching `ios-simulator-log-...` artifact of each job contains the
Simulator log. These jobs do not test input, audio or background handling on
physical devices.

## Host Code

Do not call `Engine.Start()`: Godot's view controller runs the loop. The hosts
use the UIKit scene lifecycle. They start the engine when the window scene
connects, and they access the scene after the engine starts:

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

iOS links extensions into the app. List a static library under the `ios.arm64`
key of the `.gdextension` file. You can also list an `.xcframework` of static
libraries with device and Simulator slices. `[dependencies]` entries link the
same way. The
build force-loads the slice for the `RuntimeIdentifier` and keeps the entry
symbol, which Godot resolves from the app itself. Every listed file must exist,
because the pack export copies it. Opt out with `TwoDogIosGdExtensions=false`.

## Limitations

- Native AOT (`PublishAot`) is not supported; builds use Mono.
- Physical devices, input and background handling are not tested yet.

To embed the game in a MAUI page instead, use the [MAUI host](./maui).
