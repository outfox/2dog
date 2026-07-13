# 2dog Templates

This directory contains the `dotnet new` templates for creating 2dog projects.

## Template Overview

The `twodog` template creates a complete 2dog application with:

- **Program.cs** - Minimal working 2dog application
- **Sample Godot project** - Basic project.godot with a simple scene
- **.editorconfig** - Standard .NET coding conventions
- **.gitignore** - Ignores for .NET and Godot artifacts
- **Test project** - xUnit (v3) tests with 2dog.xunit collection fixtures (included by default; `--tests false` to omit)
- **Web host project** - Browser (WebAssembly) host that publishes the game as a static site (included by default; `--web false` to omit)

## Local Development

### Installing the Template Locally

```bash
# From the repository root
dotnet new install ./templates/twodog
```

### Creating Projects

```bash
# Full project (app + Godot project + tests + web host)
dotnet new 2dog -n MyGame

# Without the test project
dotnet new 2dog -n MyGame --tests false

# Without the web host (e.g. no .NET 10 SDK / wasm-tools on this machine)
dotnet new 2dog -n MyGame --web false
```

### Uninstalling the Template

```bash
dotnet new uninstall ./templates/twodog
```

## Template Structure

```
templates/
└── twodog/                        # Template content
    ├── .template.config/
    │   └── template.json          # Template configuration
    ├── Company.Product1.sln
    ├── Company.Product1/          # Application project
    │   ├── Company.Product1.csproj
    │   └── Program.cs
    ├── Company.Product1.Godot/    # Sample Godot project
    │   ├── project.godot
    │   └── main.tscn
    ├── Company.Product1.Tests/    # Test project (default; --tests false to omit)
    │   ├── Company.Product1.Tests.csproj
    │   └── BasicTests.cs
    ├── Company.Product1.Web/      # Browser (wasm) host (default; --web false to omit)
    │   ├── Company.Product1.Web.csproj
    │   ├── Program.cs
    │   ├── global.json
    │   └── wwwroot/index.html
    ├── .editorconfig
    └── .gitignore
```

## Template Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--tests` | bool | true | Include a test project with xUnit (v3) and 2dog.xunit collection fixtures |
| `--web` | bool | true | Include a browser (WebAssembly) host project (building it requires a .NET 10+ SDK with the wasm-tools workload) |
| `--skipRestore` | bool | false | Skip automatic NuGet restore after creation |

## Symbol Replacements

The template uses `Company.Product1` as the source name, which gets replaced with your project name:

- `Company.Product1` → Your project name (e.g., `LetsCook`)
- Applied to: `.csproj` files, `Program.cs`, `project.godot`, namespaces

## Packaging

The template content is bundled directly into the main `2dog` NuGet package (from `twodog/twodog.csproj`). There is no separate template package.

When the `2dog` package is packed, the template files are included under `content/twodog/` in the `.nupkg`, and the version placeholder in `template.json` is substituted automatically.

### Installing from NuGet

```bash
dotnet new install 2dog
```

## Known Limitations

### Test Project Requires 2dog.xunit Package

The test project (included by default) references `2dog.xunit`, which needs to be packaged and published separately.

**To make it resolve:**

1. Package `2dog.xunit` as a NuGet package (it depends on `2dog`, which carries the fixtures)
2. Publish it to NuGet or a local feed
3. Ensure it's available when creating projects

**Alternatively**, for local development:

- Create the project with `--tests false`
- Manually add a test project with project references to local 2dog projects

### Web Host Requires .NET 10+ and wasm-tools

The web host project (included by default) targets `net10.0` with
`RuntimeIdentifier=browser-wasm`. Building/publishing it requires a .NET 10+
SDK with the wasm-tools workload (`dotnet workload install wasm-tools`). The
rest of the solution builds fine without it; create with `--web false` to omit
the project entirely.

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
      "defaultValue": "true"
    },
    "web": {
      "type": "parameter",
      "datatype": "bool",
      "description": "Include a browser (WebAssembly) host project",
      "defaultValue": "true"
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
