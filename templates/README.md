# 2dog Templates

This directory contains the `dotnet new` templates for creating 2dog projects.

## Template Overview

The `twodog` template creates a complete 2dog application with:

- **Program.cs** - Minimal working 2dog application
- **Sample Godot project** - Basic project.godot with a simple scene
- **.editorconfig** - Standard .NET coding conventions
- **.gitignore** - Ignores for .NET and Godot artifacts
- **Optional test project** - xUnit tests with twodog.xunit fixtures (use `--tests` flag)

## Local Development

### Installing the Template Locally

```bash
# From the repository root
dotnet new install ./templates/twodog
```

### Creating Projects

```bash
# Basic project
dotnet new 2dog -n MyGame

# With tests (requires 2dog.xunit package)
dotnet new 2dog -n MyGame --tests
```

### Uninstalling the Template

```bash
dotnet new uninstall ./templates/twodog
```

## Template Structure

```
templates/
├── 2dog.Templates.csproj    # Packaging project for NuGet distribution
└── twodog/                   # Template content
    ├── .template.config/
    │   └── template.json     # Template configuration
    ├── Company.Product1.csproj
    ├── Program.cs
    ├── .editorconfig
    ├── .gitignore
    ├── project/              # Sample Godot project
    │   ├── project.godot
    │   └── main.tscn
    └── Company.Product1.Tests/  # Optional test project
        ├── Company.Product1.Tests.csproj
        └── BasicTests.cs
```

## Template Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--tests` | bool | false | Include a test project with xUnit and twodog.xunit fixtures |
| `--skip-restore` | bool | false | Skip automatic NuGet restore after creation |

## Symbol Replacements

The template uses `Company.Product1` as the source name, which gets replaced with your project name:

- `Company.Product1` → Your project name (e.g., `LetsCook`)
- Applied to: `.csproj` files, `Program.cs`, `project.godot`, namespaces

## Packaging the Template

### Build the Template Package

```bash
# From the repository root
dotnet pack templates/2dog.Templates.csproj
```

This creates `packages/2dog.Templates.0.1.0-pre.nupkg`.

### Installing from Package

```bash
dotnet new install 2dog.Templates
```

### Publishing to NuGet

```bash
dotnet nuget push packages/2dog.Templates.0.1.0-pre.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_KEY
```

## Known Limitations

### `--tests` Requires 2dog.xunit Package

The `--tests` option creates a test project that references `2dog.xunit`, which needs to be packaged and published separately.

**To enable the test option:**

1. Package `twodog.xunit` as a NuGet package
2. Publish it to NuGet or a local feed
3. Ensure it's available when creating projects with `--tests`

**Alternatively**, for local development:

- Create the project without `--tests`
- Manually add a test project with project references to local 2dog projects

## Template Configuration

The template is defined in `.template.config/template.json`:

```json
{
  "identity": "outfox.2dog.Template",
  "name": "2dog Application",
  "shortName": "2dog",
  "sourceName": "Company.Product1",
  "symbols": {
    "tests": {
      "type": "parameter",
      "datatype": "bool",
      "description": "Include a test project",
      "defaultValue": "false"
    }
  }
}
```

## Testing Changes

After modifying the template:

1. **Uninstall the old version:**
   ```bash
   dotnet new uninstall ./templates/twodog
   ```

2. **Reinstall:**
   ```bash
   dotnet new install ./templates/twodog
   ```

3. **Test project creation:**
   ```bash
   mkdir test_project
   cd test_project
   dotnet new 2dog -n TestApp
   dotnet build
   dotnet run --project TestApp
   ```

## Future Enhancements

Potential improvements for the template:

- [ ] Add `--empty` option for projects without sample Godot content
- [ ] Add `--minimal` option for absolute bare minimum
- [ ] Add solution file generation when `--tests` is used
- [ ] Support for multiple build configurations (Debug/Release/Editor)
- [ ] Optional CI/CD workflow files (GitHub Actions, etc.)
- [ ] Optional Docker support
