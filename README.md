# How *do you* pronounce `Godot`? 🦴

<p align="center">
  <img src="docs/content/public/logo-animated.svg" alt="2dog logotype, a white stylized dog with the negative space around its leg forming the number 2, and a playful font spelling the word dog" width="70%">
</p>

[![Discord Invite](https://img.shields.io/badge/discord-_%E2%A4%9Coutfox%E2%A4%8F-blue?logo=discord&logoColor=f5f5f5)](https://discord.gg/GAXdbZCNGT)
[![NuGet](https://img.shields.io/nuget/v/2dog?color=blue)](https://www.nuget.org/packages/2dog/)
[![Build Status](https://github.com/outfox/2dog/actions/workflows/build-natives.yml/badge.svg)](https://github.com/outfox/2dog/actions/workflows/build-natives.yml)
[![CI](https://github.com/outfox/2dog/actions/workflows/ci.yml/badge.svg)](https://github.com/outfox/2dog/actions/workflows/ci.yml)

> *"Godot, or to dog... is it even a question?"*

**Bring your existing C# Godot game to the web. Keep your scenes, scripts, and
Godot workflow.**

2dog packages Godot as a library hosted by your .NET application. That
inversion makes browser publishing, ordinary `dotnet` tooling, and first-class
xUnit testing possible without rewriting your game.

## Same Game, New Tricks

If you already build games with Godot and C#, most of your world stays exactly
where it is:

- Your Godot project remains a Godot project and still opens in the editor.
- Your scenes, resources, C# scripts, signals, and exports keep working.
- You still use the familiar GodotSharp API.
- Asset import happens automatically during `dotnet build`.

What changes is who holds the leash: your .NET process starts Godot, drives its
main loop, and decides when it stops.

That unlocks a few useful tricks:

- 🌐 **C# games on the web**: publish a static WebAssembly site with `dotnet publish`.
- 🧪 **Real test projects**: load scenes through xUnit and run headless in CI.
- 🔄 **A .NET-owned lifecycle**: embed Godot in an app, server, tool, or custom host.
- 🎮 **The full engine**: scenes, physics, rendering, audio, input, and the GodotSharp API.

## Choose Your Starting Point

Both routes produce the same strongly recommended layout: the Godot project is
the solution root, with desktop, browser, and test hosts nested inside it.

### Bring an Existing Project (Recommended)

Convert in place. 2dog preserves your existing game content, and there is no
tool installation step. A classic `.sln` is migrated to `.slnx` when present:

```bash
dnx 2dog convert path/to/MyGame
cd path/to/MyGame
dotnet run --project MyGame.2dog
```

### Start a New Project

Register the project template once, then create the same complete layout from
scratch:

```bash
dotnet new install 2dog
dotnet new 2dog -n MyGame
cd MyGame
dotnet run --project MyGame.2dog
```

In either case, your original Godot workflow is still there whenever you need
it:

```bash
godot-mono --editor .
```

## From Godot Project to Browser

The generated web host publishes your C# game as a static site. Install the
.NET WebAssembly tools once, then publish and serve the bundle:

```bash
dotnet workload install wasm-tools
dotnet tool install --global dotnet-serve
dotnet publish MyGame.web
dotnet serve --directory MyGame.web/AppBundle
```

See [Web / Browser](https://2dog.dev/web.html) for the development loop,
deployment options, and current limitations.

## One Project, Three Hosts

```text
MyGame/                       Godot project and solution root
├── project.godot             Scenes, scripts, assets, project settings
├── MyGame.csproj             Godot C# game assembly
├── MyGame.2dog/              Desktop .NET host
├── MyGame.web/               Browser WebAssembly host
└── MyGame.tests/             Headless xUnit host
```

The nested hosts carry `.gdignore`, so Godot ignores them. Your game project
remains clean and editor-friendly while each host gets its own entry point and
dependencies.

Read [The Recommended Project Layout](https://2dog.dev/project-layout.html) for
the complete mental model.

## Pick Your Next Trick

- [Get Started](https://2dog.dev/getting-started.html): convert or create, run, test, and publish.
- [Converting a Godot Project](https://2dog.dev/convert.html): understand exactly what `2dog convert` changes.
- [Web / Browser](https://2dog.dev/web.html): publish your C# Godot game as a static site.
- [Testing with xUnit](https://2dog.dev/testing.html): load scenes and run Godot headlessly in CI.
- [Core Concepts](https://2dog.dev/concepts.html): learn how .NET takes control of Godot.
- [Configuration](https://2dog.dev/configuration.html): configure project paths and native variants.
- [API Reference](https://2dog.dev/api-reference.html): work directly with `Engine` and `GodotInstance`.

Full documentation lives at **[2dog.dev](https://2dog.dev)**.

## Requirements and Status

- .NET SDK 10.0 or later
- Godot .NET editor only when you want to edit scenes visually
- Supported platforms: `win-x64`, `linux-x64`, and `osx-arm64`
- Packages are available on NuGet

The `2dog` package contains the converter and project template.
Applications reference the `2dog.engine` library; test projects add
`2dog.xunit`. Generated and converted projects configure these packages for
you.

## One Dog at a Time

Only one Godot instance can run in a process at a time. Sequential restart is
supported: dispose the current instance before starting another. The supplied
xUnit collections already serialize Godot fixtures correctly.

See [Known Issues](https://2dog.dev/known-issues/) for details and workarounds.

## Teach 2dog New Tricks

Want to work on 2dog itself? Clone with submodules, then build the native and
.NET packages:

```bash
git clone --recursive https://github.com/outfox/2dog
cd 2dog
uv run poe build-all
```

Run the demo with `dotnet run --project demo/demo.2dog` and the tests with
`dotnet test twodog.tests`.

Questions, ideas, or particularly good sticks? Join the Dog Park:

[![Discord Invite](https://img.shields.io/badge/discord-_%E2%A4%9Coutfox%E2%A4%8F-blue?logo=discord&logoColor=f5f5f5)](https://discord.gg/GAXdbZCNGT)

---

### *No squirrels were harmed in the making of this README.*
