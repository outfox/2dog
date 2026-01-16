# Project Templates

2dog provides `dotnet new` templates to quickly scaffold new projects with everything configured correctly.

::: warning Pending NuGet Release
The templates described on this page are currently in development and **not yet published to NuGet**. They will be available once 2dog packages are published to NuGet.

For now, you can install templates locally from the repository:
```bash
dotnet new install ./templates/twodog
```
:::

## Installation

### From NuGet (After Release)

Once published, install the templates globally:

```bash
dotnet new install 2dog.Templates
```

### Local Installation (Development)

To use the templates during development:

```bash
# From the 2dog repository root
dotnet new install ./templates/twodog
```

## Creating Projects

### Basic Project

Create a minimal 2dog application with a sample Godot project:

```bash
dotnet new 2dog -n MyGame
cd MyGame
dotnet run
```

This creates:
- `MyGame.csproj` - Project file with 2dog package references
- `Program.cs` - Minimal working application
- `project/` - Sample Godot project with a simple scene
- `.editorconfig` - .NET coding conventions
- `.gitignore` - Standard ignores for .NET and Godot

### Project with Tests

::: warning Requires 2dog.xunit Package
The `--tests` option creates a test project that references `2dog.xunit`. This package must be available on NuGet or a local feed for restore to succeed.
:::

Create a project with an xUnit test project:

```bash
dotnet new 2dog -n MyGame --tests
```

This creates everything from the basic template plus:
- `MyGame.Tests/` - xUnit test project
  - `MyGame.Tests.csproj` - Test project with 2dog.xunit reference
  - `BasicTests.cs` - Sample tests with GodotHeadlessFixture

## Template Structure

### Generated Files

The template creates a project structure like this:

```
MyGame/
├── MyGame.csproj           # Project file
├── Program.cs              # Entry point
├── .editorconfig           # Code style settings
├── .gitignore              # Git ignores
├── project/                # Godot project
│   ├── project.godot       # Godot project file
│   └── main.tscn           # Main scene
└── MyGame.Tests/           # Optional test project
    ├── MyGame.Tests.csproj
    └── BasicTests.cs
```

### Program.cs

The generated `Program.cs` is a minimal working application:

```csharp
using Godot;
using Engine = twodog.Engine;

// Create and start the Godot engine with your project
using var engine = new Engine("MyGame", "./project");
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

The generated `.csproj` includes all necessary package references:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <OutputType>Exe</OutputType>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
    </PropertyGroup>

    <!-- 2dog package reference -->
    <ItemGroup>
        <PackageReference Include="2dog" Version="0.1.0-pre"/>
        <PackageReference Include="GodotSharp" Version="4.6.0-beta.3"/>
    </ItemGroup>

    <!-- Platform-specific native library packages -->
    <ItemGroup Condition="$([MSBuild]::IsOSPlatform('Windows'))">
        <PackageReference Include="2dog.win-x64" Version="0.1.0-pre"/>
    </ItemGroup>
    <ItemGroup Condition="$([MSBuild]::IsOSPlatform('Linux'))">
        <PackageReference Include="2dog.linux-x64" Version="0.1.0-pre"/>
    </ItemGroup>
    <ItemGroup Condition="$([MSBuild]::IsOSPlatform('OSX'))">
        <PackageReference Include="2dog.osx-x64" Version="0.1.0-pre"/>
    </ItemGroup>

    <!-- Godot project location -->
    <PropertyGroup>
        <GodotProjectDir>./project</GodotProjectDir>
    </PropertyGroup>
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
| `--tests` | bool | `false` | Include a test project with xUnit and 2dog.xunit |
| `--skip-restore` | bool | `false` | Skip automatic NuGet package restore |

### Examples

```bash
# Basic project
dotnet new 2dog -n CoolGame

# With tests
dotnet new 2dog -n CoolGame --tests

# Skip automatic restore (useful for CI)
dotnet new 2dog -n CoolGame --skip-restore
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

### Running Tests (if --tests was used)

::: warning
Tests require `2dog.xunit` package to be available. See the [Testing](/testing) guide for details on test fixtures and setup.
:::

```bash
# Run all tests
dotnet test

# Run with specific configuration
dotnet test -c Debug
dotnet test -c Editor
```

### Customizing the Godot Project

The generated `project/` directory contains a minimal Godot project. You can:

1. **Edit in Godot Editor:**
   ```bash
   # Open the project in Godot Editor (if you have TOOLS_ENABLED build)
   cd project
   godot --editor
   ```

2. **Replace with existing project:**
   ```bash
   # Remove the sample project
   rm -rf project
   
   # Copy your existing Godot project
   cp -r /path/to/your/godot/project ./project
   ```

3. **Add C# scripts:**
   - Create `.cs` files in the `project/` directory
   - Set the `GodotProjectDir` property in your `.csproj`
   - Reference the Godot project in your main project

## Uninstalling Templates

### From NuGet (After Release)

```bash
dotnet new uninstall 2dog.Templates
```

### Local Installation

```bash
dotnet new uninstall ./templates/twodog
```

Or find the installed template path:

```bash
dotnet new uninstall
# Look for the 2dog template in the list
# Copy the install path
dotnet new uninstall <install-path>
```

## Updating Templates

### From NuGet (After Release)

```bash
# Update to latest version
dotnet new update
```

### Local Installation

Uninstall and reinstall:

```bash
dotnet new uninstall ./templates/twodog
dotnet new install ./templates/twodog
```

## Troubleshooting

### Template Not Found

**Problem:** `dotnet new 2dog` shows "No templates or subcommands found"

**Solution:**
1. Ensure the template is installed: `dotnet new list | grep 2dog`
2. If not listed, install it: `dotnet new install 2dog.Templates` (or local path)

### Package Restore Failed (--tests)

**Problem:** Test project fails to restore with "Unable to find package 2dog.xunit"

**Solution:**
The `2dog.xunit` package must be available on NuGet or a local feed. Until it's published:
1. Create the project without `--tests`
2. Manually add a test project later using project references
3. See [Testing](/testing) guide for manual setup

### Wrong Package Versions

**Problem:** Generated project references outdated package versions

**Solution:**
Update package references in the `.csproj` file:

```bash
# Update all 2dog packages
dotnet add package 2dog
dotnet add package 2dog.win-x64  # or linux-x64/osx-x64
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
5. **Configure builds** - Set up Debug/Release/Editor configurations

See the [Getting Started](/getting-started) guide for a deeper walkthrough of 2dog development.
