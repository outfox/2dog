# 2dog.ios

Experimental iOS host support for [2dog](https://2dog.dev). A .NET for iOS app
links the Godot engine statically (`2dog.ios-arm64` for devices,
`2dog.iossimulator-arm64` for the Simulator) and shows Godot's own view
controller; its display link runs every frame on the main thread.

```csharp
IosHost.Register(TwoDogWebBoot.PluginsInitializer());   // or let GodotHost.Start do it
window.RootViewController = GodotHost.Start("MyGame", TwoDogWebBoot.PluginsInitializer());
GodotHost.Started += () => GodotHost.Dispatch(tree => tree.Root.PrintTree());
```

`dnx 2dog add --ios` scaffolds such a host. The build exports the game pack with
the project's `iOS` export preset, bundles it as `game.pck` and roots
`GodotSharp`, `twodog`, `twodog.ios` and the game assembly for the trimmer.
Debug builds run the Mono interpreter (`UseInterpreter`), Release builds are
AOT-compiled. Never call `Engine.Start()` on iOS.

Building needs a Mac with Xcode (the iOS SDK, clang and `codesign`) and the
`ios` workload; `dotnet build -r iossimulator-arm64` runs in the Simulator
without signing. See https://2dog.dev/hosts/ios.
