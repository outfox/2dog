# Let's take `Godot` for walkies 🦴

![a white dog smiling over the soft logotype text '2dog'](/logo-animated.svg)

## Prerequisites

- .NET SDK 8.0 or later
- A Godot project directory (with `project.godot`)

## Installation

### Building from Source

1. Clone with submodules:

```bash
git clone --recursive https://github.com/outfox/2dog
cd 2dog
```

2. Build Godot (requires Python with uv):

```bash
uv run poe build-godot
```

3. Build 2dog platform packages, and the main library and NuGet packages:

```bash
uv run poe build
```

> You can also run `uv run poe build-all` to do steps 2 and 3 in one go.

## Your First 2dog Application

::: tip Using Templates (Recommended)
The fastest way to get started is using the 2dog project template:

```bash
dotnet new install 2dog.Templates  # Install template (pending NuGet release)
dotnet new 2dog -n MyGodotApp      # Create project
cd MyGodotApp
dotnet run                          # Run the app
```

This creates a complete project with sample Godot content and everything configured. See [Project Templates](./templates) for details.

**Note:** Templates are pending NuGet release. For now, [install locally from source](./templates#local-installation-development).
:::

### Manual Setup

Alternatively, create a new console application manually:

```bash
dotnet new console -n MyGodotApp
cd MyGodotApp
dotnet add package 2dog
```

Replace `Program.cs`:

```csharp
using twodog;

// Create engine pointing to your Godot project
using var engine = new Engine("MyGodotApp", "./project");

// Start Godot
using var godot = engine.Start();

// Run the main loop
while (!godot.Iteration())
{
    // Your code runs here every frame
    // Access the scene tree via engine.Tree
}
```

## Project Structure

A minimal 2dog project requires:

```
MyGodotApp/
├── MyGodotApp.csproj
├── Program.cs
└── project/
    └── project.godot    # Minimal Godot project file
```

The `project/` directory must contain at least a `project.godot` file. You can create one with the Godot editor or use a minimal template:

```ini
; project.godot
config_version=5

[application]
config/name="MyGodotApp"
```

## Build Configurations

2dog supports three build configurations for different use cases:

```bash
dotnet build -c Debug    # Development with debug symbols
dotnet build -c Release  # Optimized production build
dotnet build -c Editor   # Editor tools with TOOLS_ENABLED
```

The **Editor** configuration enables Godot's full editor toolchain, including:
- Resource import pipeline
- Editor APIs (`EditorInterface`, `EditorPlugin`)
- Import plugins for textures, models, audio
- Scene validation and manipulation tools

See [Build Configurations](./build-configurations) for detailed information.

## Next Steps

- Use [Project Templates](./templates) to quickly scaffold new projects
- Learn about [Core Concepts](./concepts) to understand the architecture
- Explore [Build Configurations](./build-configurations) for Debug, Release, and Editor modes
- Check the [API Reference](./api-reference) for detailed documentation
- Set up [Testing with xUnit](./testing) for your project
