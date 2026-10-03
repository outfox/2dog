---
title: 2dog (generic .NET)
description: "Run and publish your Godot game as a .NET console application."
---

# 2dog (generic .NET)

The generic .NET host is a console application that runs Godot on Windows,
Linux, or macOS. A Godot window will open (unless headless).

Edit `MyGame.2dog/Program.cs` to add your own host code. 

It's a simple way to just add C# code around or on top of your Godot game and comfortably run, test, and debug it inside your IDE or using the `dotnet` CLI.

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

For a build that requires an installed .NET runtime, pass
`-p:PublishSelfContained=false`. Single-file publishing is unsupported.

### Native AOT

Native AOT compiles the host, your game, and GodotSharp into one native
executable. It needs no .NET runtime and starts faster:

```bash
dotnet publish MyGame.2dog -c Release -r win-x64 -p:PublishAot=true
```

The publish folder holds the executable, the engine, and the game pack. To
make it the default, set `<PublishAot>true</PublishAot>` in the host's
`.csproj`. See [Native AOT](/configuration#native-aot) for requirements.

See [Engine](/api/engine) to customize the frame loop and
[Build Variants](/build-configurations) to select an engine build.

## No Console

End users of generic 2dog apps usually don't need or expect a console window. The
generated host already builds Release as `WinExe`, which hides it on Windows:
```xml
<OutputType Condition="'$(Configuration)' == 'Release'">WinExe</OutputType>
```
Linux and macOS ignore `WinExe`. On Windows, console output then goes nowhere
unless it is redirected.
