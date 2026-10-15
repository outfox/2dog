---
title: MAUI
description: "Embed Godot in a .NET MAUI app for Windows and Android with MAUI controls over the game."
---

# MAUI

The MAUI host embeds a Godot view inside a .NET MAUI page on Windows and
Android. MAUI controls can sit beside or overlap the game. Both platforms
reference the same game project.

## Use It

Install the workloads for the platforms you build:

```bash
dotnet workload install maui-windows maui-android
dnx 2dog add --maui
dotnet run --project MyGame.maui -f net10.0-windows10.0.19041.0
```

On Linux or macOS, install `maui-android` and build the Android target. Windows
is included in the generated project's target frameworks only on Windows.
iOS and Mac Catalyst are not supported by this host.

For a device or an x64 Android emulator:

```bash
dotnet publish MyGame.maui -f net10.0-android -c Release -r android-arm64
dotnet build MyGame.maui -f net10.0-android -r android-x64
```

Android needs the [SDK and JDK setup](/hosts/android#set-it-up) used by the
Android host. The generated Android application registers .NET with Godot
before creating its first view. Never call `Engine.Start()` on Android.

MAUI projects are excluded from ordinary solution builds. Build the project
directly with `-f` to select its platform.

Scaffolding creates a .NET 10 `global.json` SDK pin when the project has none.
An existing root pin is preserved; building from inside `MyGame.maui` uses
the host's own .NET 10 pin.

## Customize It

`2dog.maui` provides `GodotView` and `UseTwoDog()`. The generated `MauiProgram`
registers the view handler:

```csharp
using twodog.Maui;

var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>().UseTwoDog();
```

Create a view with the game's assembly name, then add it to a MAUI layout:

```csharp
var game = new GodotView { Project = "MyGame" };
var pause = new Button { Text = "Pause", IsEnabled = false };
game.Started += (_, _) => pause.IsEnabled = true;
pause.Clicked += (_, _) => game.Dispatch(tree => tree.Paused = true);
```

The view also works in XAML. Use the namespace
`clr-namespace:twodog.Maui;assembly=twodog.maui`, then add
`<dog:GodotView Project="MyGame" />` with your chosen namespace prefix.

Use `Dispatch` for all host access to game state. Windows runs commands on
its engine's UI thread; Android runs them on Godot's render thread. The
`Started`, `QuitRequested`, and `Failed` events are delivered on the MAUI UI
thread. Do not dispatch commands before `Started`.

See the [MAUI showcase](https://github.com/outfox/2dog/tree/main/demos/showcase/showcase.maui)
for an overlay with pause and time-scale controls.

## Rendering

Windows prefers Vulkan with a shared D3D11 texture and a WinUI
`SwapChainPanel`. Godot copies its viewport to the shared texture; D3D11
copies that texture to the composition swap chain. These are GPU-side
transfers with no CPU frame readback. This is not a claim that rendering
uses zero GPU copies.

The default `PresentationMode.Auto` falls back to CPU readback if GPU
sharing is unavailable or fails. MAUI controls continue to composite over
the game. CPU mode copies and converts each frame and can reduce frame
rate at larger resolutions. `ActivePresentationMode` reports the selected
path. Set `PresentationMode.Gpu` to require GPU sharing or
`PresentationMode.Cpu` to select CPU presentation explicitly.

Android renders directly to Godot's native GPU surface, with no frame copy
through .NET. Godot handles native input and the Android lifecycle.

## Lifecycle and Limits

One active Godot view is supported per process. On Windows, unloading the
view stops the engine. On Android, detaching a view keeps the Activity's
engine and fragment alive for reattachment. Destroying the Activity stops
it; restarting Godot in that Android process is unsupported.

Windows forwards mouse, touch, and basic keyboard input. IME composition
and captured mouse input are not implemented. Android uses Godot's native
input handling. The view is intended for ordinary page layouts; complex
surface transforms and clipping on Android depend on native surface support.

See [MAUI Configuration](/configuration/maui) for platform properties and
publishing settings.
