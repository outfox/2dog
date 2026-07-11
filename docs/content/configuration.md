# Configuration

2dog uses MSBuild properties for configuration. Set these in your `.csproj` file.

## Build Configurations

2dog supports three build configurations that map to different Godot native library builds:

| Configuration | Godot Build Type | TOOLS_ENABLED | Use Case |
|--------------|------------------|---------------|----------|
| **Debug** | `template_debug` | ❌ No | Development with debug symbols |
| **Release** | `template_release` | ❌ No | Optimized runtime (games, apps) |
| **Editor** | `editor` | ✅ Yes | Import pipeline, editor APIs, tools |

### Build Configuration Usage

```bash
# Development with debug symbols
dotnet build -c Debug
dotnet test -c Debug

# Optimized release build
dotnet build -c Release

# Editor build with TOOLS_ENABLED
dotnet build -c Editor
dotnet run -c Editor
```

### What is TOOLS_ENABLED?

`TOOLS_ENABLED` is a Godot compile flag that enables the full editor toolchain:

- **Resource Import Pipeline**: Process and import assets (textures, models, audio)
- **Editor APIs**: Access to `EditorInterface`, `EditorPlugin`, `EditorScript`
- **Import Plugins**: Custom importers like `ResourceImporterTexture`
- **Scene Tools**: Advanced scene manipulation and validation
- **Export Tools**: Game export and packaging APIs

::: warning Performance Impact
Editor builds are larger and slower than template builds. Use them only when you need editor-specific features.
:::

::: tip When to Use Editor Configuration
- Building asset import/conversion tools
- Creating custom Godot editor plugins
- Processing game assets in CI/CD pipelines
- Validating scene files programmatically
- Extending Godot's editor functionality
:::

## Native Library Options

Native libraries are delivered as NuGet platform packages. Referencing `2dog`
automatically pulls in the `template_release` package for your OS
(`2dog.win-x64`, `2dog.linux-x64`, or `2dog.osx-arm64`). For debug or editor
natives, reference the corresponding variant package explicitly:

```xml
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <PackageReference Include="2dog.win-x64.debug" Version="4.7.0"/>
</ItemGroup>
<ItemGroup Condition="'$(Configuration)' == 'Editor'">
  <PackageReference Include="2dog.win-x64.editor" Version="4.7.0"/>
</ItemGroup>
```

### TwoDogVariant

Selects which native variant your project targets: `release` (default),
`debug`, or `editor`. This controls how the GodotPlugins assemblies are laid
out in your output directory (see [Directory Structure Requirements](#directory-structure-requirements)).

```xml
<PropertyGroup>
  <TwoDogVariant>editor</TwoDogVariant>
</PropertyGroup>
```

### TwoDogBuildType

Advanced: overrides the Godot build type used for API assembly placement.
Derived from `TwoDogVariant` by default, so most projects never set it directly.

| Value | Description | TOOLS_ENABLED |
|-------|-------------|---------------|
| `template_release` | Optimized release build (default) | No |
| `template_debug` | Debug build with symbols | No |
| `editor` | Full editor build with tools | Yes |

```xml
<PropertyGroup>
  <TwoDogBuildType>editor</TwoDogBuildType>
</PropertyGroup>
```

::: info Automatic Configuration
When building from the solution, `TwoDogBuildType` is automatically set:
- Debug configuration → `template_debug`
- Release configuration → `template_release`
- Editor configuration → `editor`
:::

## Project Setup

### GodotProjectDir

Points 2dog at the directory containing your `project.godot`. The path is
embedded as assembly metadata at build time and resolved at runtime via
`Engine.ResolveProjectDir()`.

```xml
<PropertyGroup>
  <GodotProjectDir>../MyGame.Godot/</GodotProjectDir>
</PropertyGroup>
```

### GODOTSHARP_DIR (environment variable)

At startup, 2dog points `GODOTSHARP_DIR` at the directory containing
`GodotPlugins.dll` so Godot's native code can find it  –  important for
non-standard host processes such as `dotnet test`. You can also set it
yourself to override where GodotPlugins is loaded from; when set, it takes
priority over the assemblies bundled in the NuGet package.

## Example Configurations

### Multi-Configuration Project

Support all three build types in one project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <!-- Debug: template_debug build -->
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <TwoDogBuildType>template_debug</TwoDogBuildType>
  </PropertyGroup>

  <!-- Release: template_release build (optimized) -->
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <TwoDogBuildType>template_release</TwoDogBuildType>
  </PropertyGroup>

  <!-- Editor: editor build with TOOLS_ENABLED -->
  <PropertyGroup Condition="'$(Configuration)' == 'Editor'">
    <TwoDogBuildType>editor</TwoDogBuildType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog" Version="4.7.0.24"/>
  </ItemGroup>
</Project>
```

### Asset Import Tool

Build a tool that uses Godot's import pipeline:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <!-- Always use editor build for import tools -->
    <TwoDogBuildType>editor</TwoDogBuildType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog" Version="4.7.0.24"/>
  </ItemGroup>
</Project>
```

```bash
# Run your import tool
dotnet run -c Editor -- --import-all
```

### Local Godot Development

When working from a source checkout of the 2dog repository (referencing
`twodog` as a `ProjectReference` instead of the NuGet package), the
GodotPlugins assemblies are found automatically in
`godot/bin/GodotSharp/Api/Debug/`. Setting the `GODOTSHARP_DIR` environment
variable overrides this lookup.

## GodotSharp API Assemblies

The GodotSharp API assemblies are automatically copied to your output directory. The configuration (Debug/Release) is determined by the native library build type, not your .NET configuration.

::: info
All libgodot shared library builds use Debug GodotSharp assemblies due to the `LIBGODOT_HOSTFXR` code path.
:::

## Directory Structure Requirements

Godot's native code has specific expectations for where the GodotSharp API assemblies must be located. This depends on how the native library was compiled.

### Editor Builds vs Template Builds

| Build Type | Compile Flags | Expected Directory Structure |
|------------|---------------|------------------------------|
| `editor` | `TOOLS_ENABLED` + `LIBGODOT_HOSTFXR` | `GodotSharp/Api/Debug/` subdirectory |
| `template_debug` | `LIBGODOT_HOSTFXR` only | `GodotSharp/Api/Debug/` subdirectory |
| `template_release` | `LIBGODOT_HOSTFXR` only | Flat (same directory as libgodot) |

### Why This Matters

When Godot initializes its .NET runtime, it checks for the GodotSharp assemblies in a specific location determined at compile time:

**Editor builds** (`TOOLS_ENABLED`):
```
your-app/
├── your-app.exe
├── libgodot.dll
└── GodotSharp/
    └── Api/
        └── Debug/
            ├── GodotSharp.dll
            ├── GodotPlugins.dll
            └── GodotPlugins.runtimeconfig.json
```

**Template release builds** (`LIBGODOT_HOSTFXR` without `TOOLS_ENABLED`):
```
your-app/
├── your-app.exe
├── libgodot.dll
├── GodotSharp.dll
├── GodotPlugins.dll
└── GodotPlugins.runtimeconfig.json
```

::: warning Directory Must Exist
If the assemblies are in the wrong location, Godot will fail with:
```
Unable to find the .NET assemblies directory.
Make sure the '...GodotSharp/Api/Debug' directory exists and contains the .NET assemblies.
```
:::

### How 2dog Handles This

2dog automatically copies the GodotPlugins assemblies (`GodotPlugins.dll`,
`.pdb`, and `.runtimeconfig.json`) to the correct location based on your
`TwoDogBuildType`:

- **`TwoDogBuildType=editor`**: Copies to `$(OutputPath)GodotSharp/Api/Debug/`
- **`TwoDogBuildType=template_debug`**: Copies to `$(OutputPath)GodotSharp/Api/Debug/`
- **`TwoDogBuildType=template_release`**: Copies directly to `$(OutputPath)`

This happens automatically via the `TwoDogCopyGodotApi` MSBuild target (and
`TwoDogPublishGodotApi` on publish).

### Troubleshooting

If you encounter the "Unable to find .NET assemblies directory" error:

1. **Check your build type matches your native library**:
   - Using `godot.*.editor.*.dll`? Set `TwoDogBuildType=editor` or build with `-c Editor`
   - Using `godot.*.template_debug.*.dll`? Set `TwoDogBuildType=template_debug` or build with `-c Debug`
   - Using `godot.*.template_release.*.dll`? Set `TwoDogBuildType=template_release` or build with `-c Release`

2. **Verify the directory structure** in your output folder matches the expected pattern above.

3. **Clean and rebuild**:
   ```bash
   dotnet clean
   dotnet build -c Debug  # or Release or Editor
   ```

4. **Check that the source assemblies exist**:
   - For local development: `godot/bin/GodotSharp/Api/Debug/`
   - For NuGet package: the package's `build/api/` directory
