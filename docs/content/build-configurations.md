# Build Configurations

2dog supports three build configurations, each using a different Godot native library variant optimized for specific use cases.

## Overview

| Configuration | Godot Build | TOOLS_ENABLED | GD Debugger | Use Case |
|--------------|-------------|---------------|---------------|----------|
| **Debug** | `template_debug` | ❌ No | ✅ Yes | Development, debugging |
| **Release** | `template_release` | ❌ No | ❌ No | Production, optimized runtime |
| **Editor** | `editor` | ✅ Yes | ✅ Yes | Asset import, editor tools |

## Debug Configuration

The Debug configuration uses Godot's `template_debug` build, which includes debug symbols and assertions but excludes editor-specific features.

### When to Use
- Day-to-day development
- Debugging game logic
- Running unit tests
- Investigating crashes or issues

### Features
- ✅ Debug symbols enabled
- ✅ Assertions and error checking
- ✅ Full GodotSharp API
- ❌ No editor tools
- ❌ No import pipeline

### Usage
```bash
dotnet build -c Debug
dotnet run -c Debug
dotnet test -c Debug
```

## Release Configuration

The Release configuration uses Godot's `template_release` build, which is fully optimized for performance with minimal binary size.

### When to Use
- Production builds
- Performance testing
- Final game/application distribution
- CI/CD production pipelines

### Features
- ✅ Fully optimized
- ✅ Minimal binary size
- ✅ Full GodotSharp API
- ❌ No debug symbols
- ❌ No editor tools
- ❌ No import pipeline

### Usage
```bash
dotnet build -c Release
dotnet run -c Release
dotnet publish -c Release
```

## Editor Configuration

The Editor configuration uses Godot's `editor` build, which includes the complete editor toolchain with `TOOLS_ENABLED` enabled.

### When to Use
- Building asset import tools
- Creating custom Godot editor plugins
- Processing game assets in CI/CD
- Using Godot's import pipeline
- Editor scripting and automation
- Scene validation and manipulation

### Features
- ✅ Debug symbols enabled
- ✅ TOOLS_ENABLED flag
- ✅ Resource import pipeline
- ✅ Editor APIs (`EditorInterface`, `EditorPlugin`, etc.)
- ✅ Import plugins (`ResourceImporterTexture`, etc.)
- ✅ Scene tools and validation
- ✅ Export tools and packaging APIs
- ⚠️ Larger binary size
- ⚠️ Slower than template builds

### Usage
```bash
dotnet build -c Editor
dotnet run -c Editor
dotnet test -c Editor
```

### Editor API Examples

#### Tool Script
```csharp
using Godot;

// [Tool] scripts run their _Ready and _Process in the editor.
// Build with: dotnet build -c Editor
[Tool]
public partial class MyToolNode : Node
{
    [Export] public bool ReadyCalled { get; set; }

    public override void _Ready()
    {
        ReadyCalled = true;
    }
}
```

#### Resource Import

Godot's import pipeline (generating `.uid` files, processing assets) runs automatically as an incremental MSBuild step in any project that sets `<GodotProjectDir>`  –  see [Resource Import](./import-tool). It executes in a separate helper process against the editor-variant libgodot via the `libgodot_import_project` entry point. Editor runtime singletons like `EditorInterface` remain unavailable through the libgodot embedding API.

::: warning Editor Runtime Limitations
The Editor configuration provides **compile-time access** to editor types (`EditorInterface`, `EditorPlugin`, etc.) and enables `[Tool]` script execution. However, editor **runtime singletons** are not initialized in embedded libgodot mode. The import pipeline runs automatically at build time in a separate helper process (see [Resource Import](./import-tool)).
:::

## Configuration Comparison

### Binary Size

Approximate sizes for Windows x64 builds:

| Configuration | libgodot.dll Size |
|--------------|------------------|
| Release | ~67 MB |
| Debug | ~82 MB |
| Editor | ~159 MB |

### Performance

Relative performance (Release = 100%):

| Configuration | Relative Performance |
|--------------|---------------------|
| Release | 100% (fastest) |
| Debug | ~95% |
| Editor | ~85% |

::: warning Editor Build Performance
Editor builds are significantly larger and slower due to the additional tooling. Only use Editor configuration when you specifically need TOOLS_ENABLED features.
:::

## Setting Up Multi-Configuration Projects

### Selecting a Variant

Referencing `2dog` gives you the `release` natives. The variant is **not**
derived from your .NET configuration automatically  –  to use `debug` or
`editor` natives, set `TwoDogVariant` and reference the matching platform
variant package, typically conditioned on your configuration:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog" Version=":2dog-version:"/>
  </ItemGroup>

  <!-- Debug natives (opt-in) -->
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <TwoDogVariant>debug</TwoDogVariant>
  </PropertyGroup>
  <ItemGroup Condition="'$(Configuration)' == 'Debug'">
    <PackageReference Include="2dog.win-x64.debug" Version=":natives-version:"/>
  </ItemGroup>

  <!-- Editor natives (opt-in, custom 'Editor' configuration) -->
  <PropertyGroup Condition="'$(Configuration)' == 'Editor'">
    <TwoDogVariant>editor</TwoDogVariant>
  </PropertyGroup>
  <ItemGroup Condition="'$(Configuration)' == 'Editor'">
    <PackageReference Include="2dog.win-x64.editor" Version=":natives-version:"/>
  </ItemGroup>
</Project>
```

The `TwoDogBuildType` property (`template_release`, `template_debug`,
`editor`) is derived from `TwoDogVariant` and only needs to be set directly
for advanced overrides.

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Build and Test

on: [push, pull_request]

jobs:
  test-debug:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 8.0.x
      
      - name: Run Debug Tests
        run: dotnet test -c Debug
  
  test-release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 8.0.x
      
      - name: Run Release Tests
        run: dotnet test -c Release
  
  test-editor:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 8.0.x
      
      - name: Run Editor Tests
        run: dotnet test -c Editor
```

## Troubleshooting

### Wrong Build Type Error

If you see:
```
Unable to find the .NET assemblies directory.
Make sure the 'GodotSharp/Api/Debug' directory exists...
```

**Solution**: Ensure `TwoDogVariant` matches the native library variant you
reference  –  `debug` for `2dog.<rid>.debug`, `editor` for `2dog.<rid>.editor`,
`release` (the default) for the packages that come with `2dog` itself. See
[Selecting a Variant](#selecting-a-variant).

### Editor APIs Not Available

If editor types are missing at compile time:

**Solution**: Use the `editor` native variant (`TwoDogVariant=editor` plus the
`2dog.<rid>.editor` platform package). Only editor builds include
`TOOLS_ENABLED` features  –  and remember that editor runtime singletons are not
initialized in embedded libgodot mode (see the warning above).

### Performance Issues

If your application runs slowly:

**Solution**: Check if you're accidentally using Editor configuration for runtime:
```bash
# For production, use Release
dotnet build -c Release
dotnet publish -c Release
```

## Best Practices

1. **Use Debug during development** for better error messages and debugging
2. **Use Release for production** to maximize performance
3. **Use Editor only when needed** for import tools or editor extensions
4. **Match test configuration to use case**:
   - General tests: Debug configuration
   - Performance tests: Release configuration
   - Import/editor tests: Editor configuration
5. **Document configuration requirements** in your project README
6. **Cache native libraries in CI/CD** to speed up builds

## Next Steps

- See [Configuration](./configuration) for detailed MSBuild property reference
- Learn about [Testing](./testing) with different configurations
