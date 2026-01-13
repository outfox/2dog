# Configuration

2dog uses MSBuild properties for configuration. Set these in your `.csproj` file.

## Native Library Options

### TwoDogBuildType

Selects the Godot build variant.

| Value | Description |
|-------|-------------|
| `template_release` | Optimized release build (default) |
| `editor` | Full editor build with dev features |

```xml
<PropertyGroup>
  <TwoDogBuildType>editor</TwoDogBuildType>
</PropertyGroup>
```

### TwoDogDevBuild

Controls whether to use dev builds (with additional debugging features).

| Value | Description |
|-------|-------------|
| `true` | Use dev build (default for `editor`) |
| `false` | Use non-dev build (default for `template_release`) |

```xml
<PropertyGroup>
  <TwoDogDevBuild>true</TwoDogDevBuild>
</PropertyGroup>
```

### TwoDogNativesLocalPath

Use a local native library instead of downloading.

```xml
<PropertyGroup>
  <TwoDogNativesLocalPath>C:\godot\bin\libgodot.dll</TwoDogNativesLocalPath>
</PropertyGroup>
```

### TwoDogSkipNativeDownload

Skip automatic native library download. Useful for CI with pre-cached files.

```xml
<PropertyGroup>
  <TwoDogSkipNativeDownload>true</TwoDogSkipNativeDownload>
</PropertyGroup>
```

### TwoDogNativesCacheDir

Override the native library cache location. Defaults to `~/.twodog/natives/{version}/`.

```xml
<PropertyGroup>
  <TwoDogNativesCacheDir>$(MSBuildProjectDirectory)/.natives/</TwoDogNativesCacheDir>
</PropertyGroup>
```

## Download Options

### TwoDogGitHubRepo

GitHub repository for downloading releases.

```xml
<PropertyGroup>
  <TwoDogGitHubRepo>outfox/2dog</TwoDogGitHubRepo>
</PropertyGroup>
```

### TwoDogPackageVersion

Package version for download URLs and cache paths.

```xml
<PropertyGroup>
  <TwoDogPackageVersion>0.1.0-pre</TwoDogPackageVersion>
</PropertyGroup>
```

## Platform Detection

These are automatically detected but can be overridden:

| Property | Description |
|----------|-------------|
| `TwoDogIsWindows` | `true` on Windows |
| `TwoDogIsLinux` | `true` on Linux |
| `TwoDogIsOSX` | `true` on macOS |
| `TwoDogArch` | Architecture: `X64` or `Arm64` |

## Example Configurations

### CI/CD Build

```xml
<PropertyGroup>
  <TwoDogSkipNativeDownload>true</TwoDogSkipNativeDownload>
  <TwoDogNativesLocalPath>$(CI_CACHE)/libgodot.dll</TwoDogNativesLocalPath>
</PropertyGroup>
```

### Local Godot Development

When building Godot from source alongside 2dog:

```xml
<PropertyGroup>
  <TwoDogSkipNativeDownload>true</TwoDogSkipNativeDownload>
  <TwoDogBuildType>editor</TwoDogBuildType>
  <TwoDogDevBuild>true</TwoDogDevBuild>
  <TwoDogNativesLocalPath>../godot/bin/godot.windows.editor.dev.x86_64.dll</TwoDogNativesLocalPath>
</PropertyGroup>
```

### Release Build

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <TwoDogBuildType>template_release</TwoDogBuildType>
  <TwoDogDevBuild>false</TwoDogDevBuild>
</PropertyGroup>
```

## GodotSharp API Assemblies

The GodotSharp API assemblies are automatically copied to your output directory. The configuration (Debug/Release) is determined by the native library build type, not your .NET configuration.

::: info
All libgodot shared library builds use Debug GodotSharp assemblies due to the `LIBGODOT_HOSTFXR` code path.
:::

## Directory Structure Requirements

Godot's native code has specific expectations for where the GodotSharp API assemblies must be located. This depends on how the native library was compiled.

### Editor Builds vs Template Builds

| Build Type | Compile Flag | Expected Directory Structure |
|------------|--------------|------------------------------|
| `editor` | `TOOLS_ENABLED` | `GodotSharp/Api/Debug/` subdirectory |
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

2dog automatically copies the GodotSharp API assemblies to the correct location based on your `TwoDogBuildType`:

- **`TwoDogBuildType=editor`**: Copies to `$(OutputPath)GodotSharp/Api/Debug/`
- **`TwoDogBuildType=template_release`**: Copies directly to `$(OutputPath)`

This happens automatically via the `TwoDogCopyGodotApi` MSBuild target.

### Troubleshooting

If you encounter the "Unable to find .NET assemblies directory" error:

1. **Check your build type matches your native library**:
   - Using `godot.*.editor.*.dll`? Set `TwoDogBuildType=editor`
   - Using `godot.*.template_release.*.dll`? Set `TwoDogBuildType=template_release`

2. **Verify the directory structure** in your output folder matches the expected pattern above.

3. **Clean and rebuild**:
   ```bash
   dotnet clean
   dotnet build
   ```

4. **Check that the source assemblies exist**:
   - For local development: `godot/bin/GodotSharp/Api/Debug/`
   - For NuGet package: The package's `contentFiles/any/any/GodotSharp/Api/Debug/`
