---
title: 2dog (generic .NET)
description: "Run and publish your Godot game as a .NET console application."
---

# 2dog (generic .NET)

The generic .NET host is a console application that runs Godot on Windows,
Linux, or macOS. Edit `MyGame.2dog/Program.cs` to add your own host code.

## Use It

From your Godot project directory:

```bash
dnx 2dog add --desktop
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

For a build that requires an installed .NET runtime, pass
`-p:PublishSelfContained=false`. Desktop Native AOT and single-file publishing
are unsupported.

See [Engine](/api/engine) to customize the frame loop and
[Build Variants](/build-configurations) to select an engine build.
