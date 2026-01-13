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
