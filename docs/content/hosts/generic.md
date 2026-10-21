---
title: 2dog (generic .NET)
description: "Run and publish your Godot game as a .NET console application."
tagline: "Learn how to run and ship your Godot game as a plain .NET app."
---

# 2dog (generic .NET)

The generic .NET host is a console application that runs Godot on Windows,
Linux, or macOS. A Godot window will open (unless headless).

It's truly the swiss-army-knife of hosts, giving you a blank .NET canvas to build on top of.

![Illus. Moritz Voss: a white anthro dog in a hacker hoodie and glasses walking their blue godot robot dog](../img/2dog-victory.png)
*It's pawesome! Robotastic!*

Edit `MyGame.2dog/Program.cs` to add C# code around or on top of an existing Godot game and comfortably run, test, and debug it inside your IDE or using the `dotnet` CLI.

## Use It

From your Godot project directory:

```bash
dnx 2dog add --generic
dotnet run --project MyGame.2dog
```

Pass Godot arguments after `--`. For a headless run:

```bash
dotnet run --project MyGame.2dog -- --headless --quit-after 300
```

## Publishing

```bash
dotnet publish MyGame.2dog -c Release
```

The publish folder includes the executable, .NET runtime, engine, and game
pack. Copy the whole folder to the target machine. Add `-r win-x64`,
`-r linux-x64`, or `-r osx-arm64` to select a platform.

Release builds hide the Windows console. See
[Desktop Publishing](/configuration#desktop-publishing) for framework-dependent,
Native AOT and ReadyToRun builds.

See [Engine](/api/engine) to customize the frame loop and
[Build Variants](/build-configurations) to select an engine build. See
[Generic .NET Configuration](/configuration/generic) for build and publish settings.

