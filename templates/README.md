# 2dog Templates

This directory contains the `dotnet new` templates for creating 2dog projects.

## Template Overview

The `twodog` template creates a complete 2dog application. The output
directory **is** the Godot project (and the solution root); the host projects
are nested inside it, each carrying a `.gdignore` so the Godot editor,
importer, and exporter skip them:

- **Sample Godot project** - project.godot, the `Godot.NET.Sdk` csproj, and a simple scene at the root
- **Desktop host** (`<Name>.2dog/`) - Minimal working 2dog application (Program.cs with Main)
- **Test project** (`<Name>.tests/`) - xUnit (v3) tests with 2dog.xunit collection fixtures (included by default; `--tests false` to omit)
- **Web host project** (`<Name>.web/`) - Browser (WebAssembly) host that publishes the game as a static site (included by default; `--web false` to omit)
- **TwoDogWebBoot.cs** - Web bootstrap compiled into the game assembly (`LIBGODOT_ENABLED`-guarded)
- **.editorconfig** - Standard .NET coding conventions
- **.gitignore** - Ignores for .NET and Godot artifacts

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
├── 2dog.Templates.csproj          # Standalone template package (2dog.Templates)
└── twodog/                        # Template content = the Godot project root
    ├── .template.config/
    │   └── template.json          # Template configuration
    ├── Company.Product1.sln       # The single solution, next to project.godot
    ├── Company.Product1.csproj    # Godot project (Godot.NET.Sdk)
    ├── project.godot
    ├── main.tscn
    ├── export_presets.cfg         # Web preset for the wasm host
    ├── global.json                # Wasm-capable SDK pin (--web false to omit)
    ├── TwoDogWebBoot.cs           # Web bootstrap (compiled into the game assembly)
    ├── Company.Product1.2dog/     # Desktop host
    │   ├── .gdignore
    │   ├── Company.Product1.2dog.csproj
    │   ├── Program.cs
    │   └── app.manifest
    ├── Company.Product1.tests/    # Test project (default; --tests false to omit)
    │   ├── .gdignore
    │   ├── Company.Product1.tests.csproj
    │   ├── BasicTests.cs
    │   └── xunit.runner.json
    ├── Company.Product1.web/      # Browser (wasm) host (default; --web false to omit)
    │   ├── .gdignore
    │   ├── Company.Product1.web.csproj
    │   ├── Program.cs
    │   ├── global.json            # Same SDK pin as the root, for runs started in here
    │   └── wwwroot/index.html
    ├── .editorconfig
    └── .gitignore
```

The host csprojs point `<GodotProjectDir>` at the parent directory (`..`) and
reference `../Company.Product1.csproj`; the Godot csproj excludes the nested
host folders via `DefaultItemExcludes`. The solution gives the web host
ActiveCfg-only entries (no `.Build.0`) so "Build Solution" works without the
wasm-tools workload  –  the web host is built explicitly with `dotnet publish`.

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

The template content in `twodog/` is the single source of truth and is packed three ways:

- Bundled into the main `2dog` NuGet package (from `twodog/twodog.csproj`), so `dotnet new install 2dog` registers the template.
- Packed standalone as the `2dog.Templates` package (from `templates/2dog.Templates.csproj`).
- Embedded into the `2dog.cli` tool, which scaffolds the same content via `2dog convert`.

When packing, the template files are included under `content/twodog/` in the `.nupkg`, and the version placeholders in `template.json` are substituted automatically.

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
   cd TestApp
   dotnet build
   dotnet run --project TestApp.2dog
   ```

## Future Enhancements

Potential improvements for the template:

- [ ] Add `--empty` option for projects without sample Godot content
- [ ] Add `--minimal` option for absolute bare minimum
- [ ] Add solution file generation when `--tests` is used
- [ ] Support for multiple build configurations (Debug/Release/Editor)
- [ ] Optional CI/CD workflow files (GitHub Actions, etc.)
- [ ] Optional Docker support
