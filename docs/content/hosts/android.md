---
title: Android
description: "Package your Godot C# game as an Android APK with the experimental Android host."
tagline: "Learn how to ship your Godot C# game as an Android app."
---

# Android

The Android host is a .NET for Android app that packages your game as an APK.
Godot's own Activity owns the screen, render loop, input, and lifecycle; the
host registers the .NET runtime before Godot starts.

::: warning Experimental
Only `android-arm64` and `android-x64` are supported.
:::

## Set It Up

```bash
dnx 2dog add --android
```

This creates `MyGame.android/` next to `project.godot`, with the project file, an
`Application` that calls `AndroidHost.Register`, and a launcher activity. It also
adds an `Android` export preset. Set `ApplicationId` in the project file to your
own package name.

You also need the .NET `android` workload (the tool offers to install it), an
Android SDK, and JDK 17.

`2dog new`, `add`, `update`, and `doctor` look for the SDK in
`AndroidSdkDirectory`,
[`ANDROID_HOME` and `ANDROID_SDK_ROOT`](https://developer.android.com/tools/variables)
and common install locations, and warn when it is missing. See
[Android SDK setup](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies)
to install the required components.

## Build and Install

```bash
dotnet publish MyGame.android -r android-arm64
adb install MyGame.android/bin/Release/net10.0-android/android-arm64/publish/*-Signed.apk
```

Publishing builds the game, exports its pack with the Android preset, and
packages a signed APK named after the `ApplicationId`. Use `-r android-x64` for an x64 emulator, and start it
with `-gpu swangle` or `-gpu host`.

See [Android Configuration](/configuration/android) for build settings and
[signing](/configuration/android#signing).

## Host Code

Do not call `Engine.Start()`: Godot runs the loop. To reach the scene from host
code, register a listener with `TwoDogActivity.addMainLoopStartedListener`. It
runs on Godot's thread once the main scene is ready.

## GDExtensions

Extensions need an Android library for each ABI, listed under `android.arm64` and
`android.x86_64` keys in their `.gdextension` file. Publishing packages them into
the APK. Every listed library must exist, because the pack export copies them.

## Trimming and AOT

Release builds are trimmed and AOT-compiled, like any .NET for Android app; see
[Trimming and Mono AOT](/configuration/android#trimming-and-mono-aot) and
experimental [Native AOT](/configuration/android#native-aot).

## Limitations

- Input, pause/resume, and process restarts are not validated yet.
