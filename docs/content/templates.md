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
dotnet run --project MyGame.2dog
```

The `MyGame/` directory **is** the Godot project  –  and the solution root. The
host projects are nested inside it, each carrying a `.gdignore` file so the
Godot editor, importer, and exporter skip them. This creates:

- `MyGame.csproj` + `project.godot` + `main.tscn` - Sample Godot project (Godot.NET.Sdk) with a simple scene
- `MyGame.sln` - The single solution, next to `project.godot`
- `TwoDogWebBoot.cs` - Web bootstrap, compiled into the game assembly (`LIBGODOT_ENABLED`-guarded)
- `MyGame.2dog/` - Desktop host with 2dog package references and a minimal `Program.cs`
- `MyGame.tests/` - xUnit test project with 2dog.xunit fixtures and sample tests
- `MyGame.web/` - Browser (wasm) host that publishes the game as a static site
- `.editorconfig` - .NET coding conventions
- `.gitignore` - Standard ignores for .NET and Godot

::: tip Web host prerequisites
Publishing `MyGame.web` requires a .NET 10+ SDK with the wasm-tools workload
(`dotnet workload install wasm-tools`)  –  see [Web / Browser](/web). The rest
of the solution builds without it: the solution file gives the web host
ActiveCfg-only entries, so "Build Solution" skips it and you build it
explicitly with `dotnet publish` when you want a web bundle.
:::

::: tip Already have a Godot project?
Use [`2dog convert`](/convert) instead  –  it produces this same layout around
your existing project, in place.
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
MyGame/                     # Godot project root = solution root
├── project.godot           # Godot project file
├── MyGame.csproj           # Godot.NET.Sdk project (assembly_name=MyGame)
├── MyGame.sln              # Solution file
├── main.tscn               # Main scene
├── export_presets.cfg      # Export presets (Web preset for the wasm host)
├── TwoDogWebBoot.cs        # Web bootstrap (compiled into the game assembly)
├── MyGame.2dog/            # Desktop host
│   ├── .gdignore           # Hides the folder from the Godot editor
│   ├── MyGame.2dog.csproj
│   └── Program.cs          # Entry point
├── MyGame.tests/           # Test project (--tests false to omit)
│   ├── .gdignore
│   ├── MyGame.tests.csproj
│   └── BasicTests.cs
├── MyGame.web/             # Browser (wasm) host (--web false to omit)
│   ├── .gdignore
│   ├── MyGame.web.csproj
│   ├── Program.cs
│   ├── global.json         # Pins a .NET 10+ SDK for this directory
│   └── wwwroot/index.html
├── .editorconfig           # Code style settings
└── .gitignore              # Git ignores
```

Every nested host folder contains a `.gdignore` file, which makes the Godot
editor, importer, and exporter treat it as invisible. In the other direction,
the Godot project's csproj excludes the host folders from its default compile
globs (`DefaultItemExcludes`), so the two project layers never swallow each
other's sources.

### Program.cs

The generated `MyGame.2dog/Program.cs` is a minimal working application:

```csharp
using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    // STA matches how godot.exe runs its main thread on Windows: OLE (drag & drop,
    // IME, native dialogs) fails to initialize on the MTA thread .NET uses by default.
    // No effect on Linux/macOS.
    [STAThread]
    private static void Main()
    {
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
    }
}
```

### Project File

The generated host `.csproj` needs a single `2dog` package reference  –  GodotSharp,
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
        <!-- The assembly keeps the folder name, but 'MyGame.2dog' is
             not a valid C# namespace (digit-leading segment). -->
        <RootNamespace>MyGame</RootNamespace>
        <!-- comctl32 v6 + long paths for Godot's Windows display server -->
        <ApplicationManifest>app.manifest</ApplicationManifest>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="2dog" Version=":2dog-version:"/>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../MyGame.csproj"/>
    </ItemGroup>

    <!-- Godot project location: the parent directory (the Godot project is the
         solution root; hosts are nested inside it, hidden from the Godot
         editor by their .gdignore) -->
    <PropertyGroup>
        <GodotProjectDir>..</GodotProjectDir>
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

The project root **is** a minimal Godot project with:

- **project.godot** - Configured for .NET/C# with proper assembly name
- **MyGame.csproj** - The `Godot.NET.Sdk` project that owns your C# scripts
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

From the project root (`MyGame/`):

```bash
# Build the solution
dotnet build

# Run the desktop host
dotnet run --project MyGame.2dog

# Run with specific configuration
dotnet build -c Release
dotnet run --project MyGame.2dog -c Release
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

The generated `MyGame/` root is a minimal Godot project. You can:

1. **Edit in Godot Editor:**
   ```bash
   # Open the project in the (external) Godot editor
   godot --editor --path MyGame
   ```
   The nested host folders are invisible to the editor thanks to their
   `.gdignore` files.

2. **Start from an existing project:**
   Instead of copying files into the template output, run
   [`2dog convert`](/convert) on your existing Godot project  –  it scaffolds
   the same nested hosts around it, in place.

3. **Add C# scripts:**
   - Create `.cs` files anywhere in the Godot project (outside the host folders)
   - They compile into the game assembly via `MyGame.csproj`
   - The hosts already reference that project

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
