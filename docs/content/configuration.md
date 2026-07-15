# Configuration

2dog uses MSBuild properties for configuration. Set these in your `.csproj` file.

## Native Variants

2dog ships three native library variants:

| Variant | Godot Build Type | TOOLS_ENABLED | Use Case |
|---------|------------------|---------------|----------|
| **release** | `template_release` | ❌ No | Optimized runtime (games, apps) |
| **debug** | `template_debug` | ❌ No | Development with debug symbols |
| **editor** | `editor` | ✅ Yes | Editor APIs, [Tool] scripts |

The variant is selected with the `TwoDogVariant` property alone  –  it is not
derived from your .NET configuration automatically. All three variants ship
with the platform meta package, so no extra package references are needed.
See [Build Configurations](./build-configurations#selecting-a-variant) for
mapping variants to your .NET configurations.

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

::: warning Editor Runtime Limitations
`TOOLS_ENABLED` provides compile-time access to editor types and enables
`[Tool]` script execution, but editor runtime singletons and the import
pipeline are not initialized in embedded libgodot mode. Asset import instead
runs automatically at build time in a separate helper process via the
`libgodot_import_project` entry point (see [Resource Import](./import-tool)).
:::

## Native Library Options

Native libraries are delivered as NuGet platform packages. Referencing `2dog.engine`
pulls in the platform meta package for your OS (`2dog.win-x64`,
`2dog.linux-x64`, or `2dog.osx-arm64`), which pins all three variant packages
(`.release`, `.debug`, `.editor`). The build copies the variant selected by
`TwoDogVariant` into your output directory as `libgodot-<variant>.dll`
(`.so`/`.dylib`), and 2dog loads it by that name at runtime  –  a missing or
mismatched variant fails with an actionable error instead of an opaque
hostfxr failure.

### TwoDogVariant

Selects which native variant your project targets: `release` (default),
`debug`, or `editor`. This single property controls which `libgodot-<variant>`
native library is copied and loaded AND how the GodotPlugins assemblies are
laid out in your output directory (see
[Directory Structure Requirements](#directory-structure-requirements)).
It is also embedded as assembly metadata
(`[AssemblyMetadata("TwoDogVariant", ...)]`) so the runtime loads the
matching native.

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


## Project Setup

### GodotProjectDir

Points 2dog at the directory containing your `project.godot`. The path is
embedded as assembly metadata at build time and resolved at runtime via
`Engine.ResolveProjectDir()`.

```xml
<PropertyGroup>
  <!-- In the standard layout the host project is nested inside the Godot
       project, so the Godot project is the parent directory -->
  <GodotProjectDir>..</GodotProjectDir>
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

To support all three native variants in one project, map your .NET
configurations to variants explicitly  –  see the complete example in
[Build Configurations: Selecting a Variant](./build-configurations#selecting-a-variant).

### Editor Tooling Project

Build a tool that uses editor types and `[Tool]` scripts (asset importing
itself happens automatically at build time  –  see
[Resource Import](./import-tool)):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <TwoDogVariant>editor</TwoDogVariant>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog.engine" Version=":2dog-version:"/>
  </ItemGroup>
</Project>
```

### Local Godot Development

When working from a source checkout of the 2dog repository (referencing
`twodog.engine` as a `ProjectReference` instead of the NuGet package), the
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
| `template_debug` | `LIBGODOT_HOSTFXR` only | Flat (same directory as libgodot) |
| `template_release` | `LIBGODOT_HOSTFXR` only | Flat (same directory as libgodot) |

### Why This Matters

When Godot initializes its .NET runtime, it checks for the GodotSharp assemblies in a specific location determined at compile time:

**Editor builds** (`TOOLS_ENABLED`):
```
your-app/
├── your-app.exe
├── libgodot-editor.dll
└── GodotSharp/
    └── Api/
        └── Debug/
            ├── GodotSharp.dll
            ├── GodotPlugins.dll
            └── GodotPlugins.runtimeconfig.json
```

**Template builds** (`LIBGODOT_HOSTFXR` without `TOOLS_ENABLED`):
```
your-app/
├── your-app.exe
├── libgodot-release.dll   (or libgodot-debug.dll)
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
- **`TwoDogBuildType=template_debug` / `template_release`**: Copies directly to `$(OutputPath)`

This happens automatically via the `TwoDogCopyGodotApi` MSBuild target (and
`TwoDogPublishGodotApi` on publish). At startup, 2dog also points
`GODOTSHARP_DIR` at whichever of the two layouts is present, so non-standard
host processes (like `dotnet test`) resolve GodotPlugins correctly.

### Troubleshooting

If you encounter the "Unable to find .NET assemblies directory" error:

1. **Check `TwoDogVariant` and the copied native agree**: the output directory
   should contain a `libgodot-<variant>` library matching your
   `TwoDogVariant`. A mismatch is reported at load time with the expected
   file name and the directories that were probed.

2. **Verify the directory structure** in your output folder matches the expected pattern above.

3. **Clean and rebuild**:
   ```bash
   dotnet clean
   dotnet build -c Debug  # or Release or Editor
   ```

4. **Check that the source assemblies exist**:
   - For local development: `godot/bin/GodotSharp/Api/Debug/`
   - For NuGet package: the package's `build/api/` directory
