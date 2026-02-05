# How *do you* pronounce `Godot` ? 🦴

<p align="center">
  <img src="docs/content/public/logo-animated.svg" alt="2dog logotype, a white stylized dog with the negative space around its leg forming the number 2, and a playful font spelling the word dog" width="70%">
</p>

[![Discord Invite](https://img.shields.io/badge/discord-_%E2%A4%9Coutfox%E2%A4%8F-blue?logo=discord&logoColor=f5f5f5)](https://discord.gg/GAXdbZCNGT)
[![NuGet](https://img.shields.io/nuget/v/2dog?color=blue)](https://www.nuget.org/packages/2dog/)
[![Build Status](https://github.com/outfox/2dog/actions/workflows/build-natives.yml/badge.svg)](https://github.com/outfox/2dog/actions/workflows/build-natives.yml)
[![CI](https://github.com/outfox/2dog/actions/workflows/ci.yml/badge.svg)](https://github.com/outfox/2dog/actions/workflows/ci.yml)

> *"Godot, or to dog... is it even a question?"*

This library lets your C# application code start and pump Godot's MainLoop - not the other way around.

---

## What is 2dog?

**2dog** is a .NET/C# front-end for [Godot Engine](https://github.com/godotengine/godot) that inverts the traditional architecture. Instead of having Godot's process and scene tree drive your application, **you** now control Godot as a library.

Think of it like this: Godot is your loyal companion that follows your lead, learns new tricks, and does exactly what you tell it to. All this while still having all the capabilities of the full engine.

```cs
using twodog;

using var engine = new Engine("myapp", "./project");
using var godot = engine.Start();

// Load a scene
var scene = GD.Load<PackedScene>("res://game.tscn");
engine.Tree.Root.AddChild(scene.Instantiate());

// Run the main loop
while (!godot.Iteration())
{
    // Your code here – every frame
}
```

### What does this mean?

- 🎮 **Full Godot Power** – access the complete GodotSharp API: scenes, physics, rendering, audio, input – everything Godot can do
- 🔄 **Inverted Control** – your .NET process controls Godot, not the other way around
- 🧪 **First-Class Testing** – built-in xUnit fixtures for testing Godot code, run headless in CI/CD pipelines

---

## Features

- Godot as an embedded library (libgodot)
- Full GodotSharp API access
- Custom .NET-first project structure
- Three build configurations: Debug, Release, and Editor (with `TOOLS_ENABLED`)
- xUnit test fixtures (`GodotFixture`, `GodotHeadlessFixture`)
- `dotnet new` project templates
- Headless mode for servers and CI/CD

> **Prerelease packages are now available on NuGet!** Linux and Windows supported. macOS is WIP.

---

## Quick Start

### Prerequisites
- .NET SDK 8.0 or later
- [Godot Mono](https://godotengine.org/) (for importing project assets)

### Using Templates (Recommended)

```bash
# Install the template (bundled in the main 2dog package)
dotnet new install 2dog

# Create a new project (optionally with xUnit tests)
dotnet new 2dog --tests True -n MyGame

# Navigate into the project
cd MyGame

# Import assets or just open with Godot Editor of your choide
godot-mono --path MyGame.Godot --import

# Run tests
dotnet test

# Run the game
dotnet run --project MyGame

# Edit in Godot at any time
godot-mono -e --path MyGame.Godot
```

### Adding to an Existing Project

```bash
dotnet add package 2dog
```

---

## Known Issues

### Single Godot Instance Per Process

Only one Godot instance can exist per process. Attempting to start a second instance will throw an `InvalidOperationException`. This is a fundamental constraint of the Godot engine.

### xUnit Test Discovery Crash with Godot Types

Using Godot types (like `NodePath`, `StringName`, `Vector2`, etc.) in xUnit `[MemberData]` will crash the test runner during discovery. This happens because xUnit enumerates test data before tests run, instantiating Godot types before the engine is initialized.

**Crashes during discovery:**
```csharp
public static IEnumerable<object[]> paths = [[new NodePath("/root")]];

[Theory]
[MemberData(nameof(paths))]
public void MyTest(NodePath path) { }
```

**Workaround:** Add `DisableDiscoveryEnumeration = true`:
```csharp
[MemberData(nameof(paths), DisableDiscoveryEnumeration = true)]
```

Or use primitive types and construct Godot objects inside the test:
```csharp
public static IEnumerable<object[]> paths = [["/root"]];

[Theory]
[MemberData(nameof(paths))]
public void MyTest(string pathStr)
{
    var path = new NodePath(pathStr);
    // ...
}
```

---

### Building from Source

If you prefer to build everything locally instead of using NuGet packages:

1. **Clone and initialize submodules**
```bash
git clone --recursive https://github.com/outfox/2dog
cd 2dog
```

2. **Build Godot** (requires Python with uv)
```bash
uv run poe build-godot
```

3. **Build .NET packages**
```bash
uv run poe build
```

> You can also run `uv run poe build-all` to do steps 2 and 3 in one go.

4. **Run the demo**
```bash
dotnet run --project demo
```

### Build Configurations

```bash
dotnet build -c Debug    # Development with debug symbols
dotnet build -c Release  # Optimized production build
dotnet build -c Editor   # Editor tools with TOOLS_ENABLED
```

> Currently tested on Linux and Windows only. macOS support is WIP.

---

## Documentation

Full documentation at **[2dog.dev](https://2dog.dev)**

- [Getting Started](https://2dog.dev/getting-started.html) – installation and first project
- [Core Concepts](https://2dog.dev/concepts.html) – architecture and design
- [Build Configurations](https://2dog.dev/build-configurations.html) – Debug, Release, and Editor modes
- [API Reference](https://2dog.dev/api-reference.html) – Engine, GodotInstance, and more
- [Testing with xUnit](https://2dog.dev/testing.html) – test fixtures and CI/CD setup
- [Project Templates](https://2dog.dev/templates.html) – scaffolding new projects

---

## Join the Pack

Questions? Ideas? Want to teach 2dog new tricks?

---

## Acknowledgements

Inspired by and built upon Ben Rog-Wilhelm's [libgodot_example](https://github.com/zorbathut/libgodot_example/tree/csharp).
*You're the GOAT. Or a [DIESEL HORSE](https://diesel.horse). Same difference!*

---

#### *No squirrels were harmed in the making of this README.*
