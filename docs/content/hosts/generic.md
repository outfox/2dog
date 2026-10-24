---
title: 2dog (generic .NET)
description: "Run and publish your Godot game as a .NET console application."
tagline: "Learn how to run and ship your Godot game as a plain .NET app."
keywords: "Godot C# console app, Native AOT, single-file publish"
---

# 2dog (generic .NET)

The generic .NET host is a console application that runs Godot on Windows,
Linux, or macOS. Godot opens a window unless you run it headless.

It is truly the Swiss Army knife of hosts: it gives you a blank .NET canvas
to build on.

![Illus. Moritz Voss: a white anthro dog in a hacker hoodie and glasses walking their blue godot robot dog](../img/2dog-victory.png)
*It's pawesome! Robotastic!*

Edit `MyGame.2dog/Program.cs` to add your own C# code to an existing Godot
game. You can run, test, and debug the game in your IDE or with the `dotnet`
CLI.

## Use It

Run these commands from your Godot project directory:

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
single-file, Native AOT and ReadyToRun builds.

See [Engine](/api/engine) to customize the frame loop and
[Build Variants](/build-configurations) to select an engine build. See
[Generic .NET Configuration](/configuration/generic) for build and publish settings.

