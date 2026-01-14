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
cd twodog
```

2. Build Godot (requires Python with uv):

```bash
uv run build.py
```

3. Build the project:

```bash
dotnet build
```

## Your First 2dog Application

Create a new console application:

```bash
dotnet new console -n MyGodotApp
cd MyGodotApp
dotnet add package twodog
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

## Next Steps

- Learn about [Core Concepts](./concepts) to understand the architecture
- Check the [API Reference](./api-reference) for detailed documentation
- Set up [Testing with xUnit](./testing) for your project
