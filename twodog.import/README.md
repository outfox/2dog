# 2dog-import

A .NET tool for importing Godot resources using libgodot. This tool generates `.uid` files for C# scripts and processes resource imports in Godot projects.

## Installation

### As a local tool (project-specific)

Add to your `dotnet-tools.json`:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "2dog.import": {
      "version": "0.1.9-pre",
      "commands": [
        "2dog-import"
      ]
    }
  }
}
```

Then restore:

```bash
dotnet tool restore
```

### As a global tool

```bash
dotnet tool install -g 2dog.Import --version 0.1.9-pre
```

## Usage

Run the import tool on a Godot project:

```bash
# Import current directory (must contain project.godot)
dotnet tool run 2dog-import

# Or if installed globally
2dog-import

# Import a specific project directory
dotnet tool run 2dog-import path/to/godot/project

# Or with --path flag
dotnet tool run 2dog-import --path path/to/godot/project
```

## How it works

Unlike the Python `import-project.py` script which invokes an external Godot editor binary, `2dog-import` uses the embedded libgodot from the 2dog native packages. This means:

- No need to build the Godot editor separately
- Uses the same libgodot version as your application
- Works in CI/CD environments without additional dependencies
- Runs in headless mode (no display required)

## For Template Users

When creating a new project with the `--editor` flag:

```bash
dotnet new 2dog -n MyGame --editor
```

You'll get a `MyGame.Editor` project that you can run directly:

```bash
dotnet run --project MyGame.Editor
```

This provides a customizable entry point for running editor operations on your Godot project.
