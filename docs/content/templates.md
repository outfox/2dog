# Project Templates

2dog provides `dotnet new` templates to quickly scaffold new projects with everything configured correctly.

## Installation

The `dotnet new` template is bundled in the main `2dog` NuGet package. Installing the package also registers the template:

```bash
dotnet new install 2dog
```

### Local Installation (Development)

To use the templates during development from a local clone:

```bash
# From the 2dog repository root
dotnet new install ./templates/twodog
```

## Creating Projects

### Full Project (Default)

Create a 2dog application with the full capability spectrum  –  desktop host,
sample Godot project, xUnit tests, and a browser (WebAssembly) host:

```bash
dotnet new 2dog -n MyGame
cd MyGame
dotnet run --project MyGame
```

This creates:
- `MyGame/MyGame.csproj` - Desktop host with 2dog package references
- `MyGame/Program.cs` - Minimal working application
- `MyGame.Godot/` - Sample Godot project with a simple scene
- `MyGame.Tests/` - xUnit test project with 2dog.xunit fixtures and sample tests
- `MyGame.Web/` - Browser (wasm) host that publishes the game as a static site
- `.editorconfig` - .NET coding conventions
- `.gitignore` - Standard ignores for .NET and Godot

::: tip Web host prerequisites
Building `MyGame.Web` requires a .NET 10+ SDK with the wasm-tools workload
(`dotnet workload install wasm-tools`)  –  see [Web / Browser](/web). The rest
of the solution builds without it.
:::

### Opting Out

Each optional project can be excluded:

```bash
# Without the test project
dotnet new 2dog -n MyGame --tests false

# Without the web host
dotnet new 2dog -n MyGame --web false
```

## Template Structure

### Generated Files

The template creates a project structure like this:

```
MyGame/
├── MyGame.sln              # Solution file
├── MyGame/                 # Application project
│   ├── MyGame.csproj       # Project file
│   └── Program.cs          # Entry point
├── MyGame.Godot/           # Godot project
│   ├── MyGame.Godot.csproj # Godot.NET.Sdk project file
│   ├── project.godot       # Godot project file
│   └── main.tscn           # Main scene
├── MyGame.Tests/           # Test project (--tests false to omit)
│   ├── MyGame.Tests.csproj
│   └── BasicTests.cs
├── MyGame.Web/             # Browser (wasm) host (--web false to omit)
│   ├── MyGame.Web.csproj
│   ├── Program.cs
│   ├── global.json         # Pins a .NET 10+ SDK for this directory
│   └── wwwroot/index.html
├── .editorconfig           # Code style settings
└── .gitignore              # Git ignores
```

### Program.cs

The generated `Program.cs` is a minimal working application:

```csharp
using Godot;
using Engine = twodog.Engine;

// Create and start the Godot engine with your project
using var engine = new Engine("MyGame", Engine.ResolveProjectDir());
using var godot = engine.Start();

// Load your main scene
var scene = GD.Load<PackedScene>("res://main.tscn");
engine.Tree.Root.AddChild(scene.Instantiate());

GD.Print("2dog is running! Close window or press 'Q' to quit.");
Console.WriteLine("Press 'Q' to quit.");

// Main game loop - runs until window closes or 'Q' is pressed
while (!godot.Iteration())
{
    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
        break;
    
    // Your per-frame logic here
}

Console.WriteLine("Shutting down...");
```

### Project File

The generated `.csproj` needs a single `2dog` package reference  –  GodotSharp,
source generators, and platform-specific native libraries are all included
transitively  –  plus a project reference to the Godot project for your C#
scripts:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <OutputType>Exe</OutputType>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <IsPackable>false</IsPackable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="2dog" Version=":2dog-version:"/>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../MyGame.Godot/MyGame.Godot.csproj"/>
    </ItemGroup>

    <!-- Godot project location -->
    <PropertyGroup>
        <GodotProjectDir>../MyGame.Godot</GodotProjectDir>
    </PropertyGroup>

    <!-- Remove duplicate Godot.SourceGenerators that come from the Godot project
         (2dog package already embeds them) -->
    <Target Name="RemoveDuplicateGodotAnalyzers" BeforeTargets="CoreCompile">
        <ItemGroup>
            <Analyzer Remove="@(Analyzer)" Condition="$([System.String]::Copy('%(Analyzer.Identity)').Contains('Godot.SourceGenerators'))" />
        </ItemGroup>
    </Target>
</Project>
```

### Sample Godot Project

The template includes a minimal Godot project with:

- **project.godot** - Configured for .NET/C# with proper assembly name
- **main.tscn** - Simple scene with a centered label showing "Hello from 2dog!"

You can replace these with your own Godot project files or edit them in the Godot editor.

## Template Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-n, --name` | string | (required) | Name of the project to create |
| `--tests` | bool | `true` | Include a test project with xUnit and 2dog.xunit |
| `--web` | bool | `true` | Include a browser (WebAssembly) host project |
| `--skipRestore` | bool | `false` | Skip automatic NuGet package restore |
| `--twodogVersion` | string | (current release) | Version of 2dog packages to reference |
| `--nativesVersion` | string | (current release) | Version of native platform packages to reference |
| `--godotVersion` | string | (current release) | Version of Godot.NET.Sdk to reference |

### Examples

```bash
# Full project (app + Godot project + tests + web host)
dotnet new 2dog -n CoolGame

# Without tests or web host
dotnet new 2dog -n CoolGame --tests false --web false

# Skip automatic restore (useful for CI)
dotnet new 2dog -n CoolGame --skipRestore
```

## Working with Generated Projects

### Building and Running

```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Run with specific configuration
dotnet build -c Release
dotnet run -c Release
```

### Running Tests

See the [Testing](/testing) guide for details on test fixtures and setup.

```bash
# Run all tests
dotnet test

# Run with specific configuration
dotnet test -c Debug
```

### Customizing the Godot Project

The generated `MyGame.Godot/` directory contains a minimal Godot project. You can:

1. **Edit in Godot Editor:**
   ```bash
   # Open the project in the (external) Godot editor
   godot --editor --path MyGame.Godot
   ```

2. **Replace with existing project:**
   ```bash
   # Remove the sample project
   rm -rf MyGame.Godot

   # Copy your existing Godot project
   cp -r /path/to/your/godot/project ./MyGame.Godot
   ```

3. **Add C# scripts:**
   - Create `.cs` files in the `MyGame.Godot/` directory
   - The `GodotProjectDir` property in your `.csproj` already points there
   - Reference the Godot project in your main project

## Uninstalling Templates

```bash
dotnet new uninstall 2dog
```

For local installations:

```bash
dotnet new uninstall ./templates/twodog
```

## Updating Templates

```bash
# Update to latest version
dotnet new update
```

For local installations, uninstall and reinstall:

```bash
dotnet new uninstall ./templates/twodog
dotnet new install ./templates/twodog
```

## Troubleshooting

### Template Not Found

**Problem:** `dotnet new 2dog` shows "No templates or subcommands found"

**Solution:**
1. Ensure the template is installed: `dotnet new list | grep 2dog`
2. If not listed, install it: `dotnet new install 2dog` (or local path)

### Wrong Package Versions

**Problem:** Generated project references outdated package versions

**Solution:**
Update the package reference in the `.csproj` file:

```bash
dotnet add package 2dog
```

Or edit the `.csproj` manually to use the latest version.

## Template Customization

If you're developing the templates themselves, see the [templates/README.md](https://github.com/outfox/2dog/tree/main/templates) in the repository for:

- Template structure and configuration
- Symbol replacements
- Testing template changes
- Packaging and publishing

## Next Steps

After creating a project from the template:

1. **Explore the sample** - Run the generated project to see 2dog in action
2. **Replace the Godot project** - Swap in your own Godot assets and scenes
3. **Add game logic** - Implement your game loop in `Program.cs`
4. **Write tests** - Add test coverage using the xUnit fixtures
5. **Configure builds** - See [Build Configurations](/build-configurations) for native variants

See the [Getting Started](/getting-started) guide for a deeper walkthrough of 2dog development.
