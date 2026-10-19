---
title: MAUI
description: "Embed Godot in a .NET MAUI page on Windows and Android."
tagline: "Learn how to embed Godot in a .NET MAUI app on Windows and Android."
---

# MAUI

Embed your game in a MAUI page, with controls beside or over the Godot view.
Windows and Android share the same game project.

## Use It

From your Godot project directory on Windows:

```bash
dotnet workload install maui-windows maui-android
dnx 2dog add --maui
dotnet run --project MyGame.maui
```

Add `-f net10.0-windows10.0.19041.0` to pick the Windows target; otherwise
`dotnet run` asks.

For Android on any supported host OS, install `maui-android` and follow the
[SDK and JDK setup](/hosts/android#set-it-up). To build for an x64 emulator:

```bash
dotnet build MyGame.maui -f net10.0-android -r android-x64
```

Build the host project directly; MAUI projects are excluded from ordinary
solution builds. iOS and Mac Catalyst are unsupported.

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

Access game state through `Dispatch` after `Started`. It runs on the engine
thread; `Started`, `QuitRequested` and `Failed` events run on the MAUI UI
thread. Android manages engine startup; never call `Engine.Start()` there.

See the [MAUI showcase](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.maui)
for pause and time-scale controls.

## Rendering and Lifecycle

Windows uses GPU texture sharing with CPU readback as a fallback. Android
renders to Godot's native GPU surface. See [view properties](/configuration/maui#view-properties)
to choose the Windows presentation mode.

One Godot view can be active per process. Unloading it stops the engine on
Windows. On Android, detaching keeps the engine alive for reattachment;
destroying the Activity stops it. Restarting Godot in that Android process
is unsupported.

Windows supports mouse, touch and basic keyboard input; IME composition and
captured mouse input are unimplemented. Android uses Godot's native input;
complex transforms and clipping depend on native surface support.

See [MAUI Configuration](/configuration/maui) for build and publish settings.
