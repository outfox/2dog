---
title: MAUI
description: "Embed Godot in a .NET MAUI page on Windows, Android and, experimentally, iOS."
tagline: "Learn how to embed Godot in a .NET MAUI app on Windows, Android and iOS."
---

# MAUI

The MAUI host embeds your game in a MAUI page, with controls beside or over
the Godot view.
Windows, Android and iOS share the same game project. iOS support is
experimental and requires a Mac with Xcode.

## Use It

On Windows, run these commands from your Godot project directory:

```bash
dotnet workload install maui-windows maui-android
dnx 2dog add --maui
dotnet run --project MyGame.maui
```

To select the Windows target, add `-f net10.0-windows10.0.19041.0`. Without
it, `dotnet run` asks you to select a target.

For Android, install `maui-android` on any supported development OS. Then
follow the [SDK and JDK setup](/hosts/android#set-it-up). To build for an x64
emulator, run this command:

```bash
dotnet build MyGame.maui -f net10.0-android -r android-x64
```

Build the host project directly, because ordinary solution builds skip MAUI
projects.

On a Mac, the host also targets `net10.0-ios` (experimental). Install
`maui-ios`. Then build for the Simulator or a device in the same way as the
[iOS host](/hosts/ios). The app delegate registers the game's plugins
initializer with `IosHost.Register` before the page creates the view. Mac
Catalyst is unsupported.

Start an iPhone or iPad Simulator. Then run these commands from `MyGame.maui/`:

```bash
dotnet workload install maui-ios
dotnet build -f net10.0-ios -r iossimulator-arm64
```

To install and launch the `.app`, follow the
[iOS host guide](/hosts/ios#build-and-run). You can also
[try a CI-built MAUI Simulator app](/hosts/ios#try-a-ci-build).

## Customize It

The generated `MauiProgram` registers `GodotView` with `UseTwoDog()`:

```csharp
using twodog.Maui;

var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>().UseTwoDog();
```

Set `Project` to your game's assembly name and add the view to a MAUI layout:

```csharp
var game = new GodotView { Project = "MyGame" };
var pause = new Button { Text = "Pause", IsEnabled = false };
game.Started += (_, _) => pause.IsEnabled = true;
pause.Clicked += (_, _) => game.Dispatch(tree => tree.Paused = true);
```

For XAML, map `dog` to `clr-namespace:twodog.Maui;assembly=twodog.maui` and use
`<dog:GodotView Project="MyGame" />`.

After `Started`, access game state through `Dispatch`. `Dispatch` runs your
code on the engine thread. The `Started`, `QuitRequested` and `Failed` events
run on the MAUI UI thread. On Android, do not call `Engine.Start()`, because
Android starts the engine.

See the [MAUI showcase](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.maui)
for pause and time-scale controls.

## Rendering and Lifecycle

On Windows, the view shares a GPU texture with the engine. If that is not
possible, it uses CPU readback. Android
renders to Godot's native GPU surface, and iOS hosts Godot's own view
controller as a child of the page. See [view properties](/configuration/maui#view-properties)
to choose the Windows presentation mode.

Only one Godot view can be active in each process. On Windows, the engine
stops when you unload the view. On Android and iOS, the engine stays alive when
you detach the view, so you can attach it again. On Android, the engine stops
when the Activity is destroyed. You cannot restart Godot in the same process
on Android or iOS.

Windows supports mouse, touch and basic keyboard input. IME composition and
captured mouse input are not supported yet. Android uses Godot's native input.
On Android, complex transforms and clipping work only if the native surface
supports them.

See [MAUI Configuration](/configuration/maui) for build and publish settings.
