# Import Tool

The `twodog.import` tool runs Godot's resource import pipeline against a project directory. This generates `.uid` files for C# scripts and processes other resources that Godot tracks.

## Why

Godot's editor assigns unique identifiers (`.uid` files) to resources including C# scripts. These are normally generated when opening a project in the Godot editor, but in a .NET-first workflow you may need to trigger imports without launching the editor UI.

`twodog.import` wraps the Godot editor binary's `--headless --import` command, providing a simple interface for build scripts and CI/CD pipelines.

::: info
This tool invokes the Godot editor binary as a subprocess rather than using the embedded libgodot engine. The `--import` flag requires Godot's full editor initialization path, which is not available through the libgodot embedding API.
:::

## Usage

```bash
dotnet run --project twodog.import -- [--editor <godot-binary>] <project-path>
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<project-path>` | Path to a directory containing `project.godot` |
| `--editor <path>` | Path to the Godot editor binary |

### Editor Binary Resolution

The tool locates the Godot editor binary using this priority order:

1. **`--editor` argument** -- explicit path passed on the command line
2. **`GODOT_EDITOR` environment variable** -- convenient for repeated use or CI/CD

If neither is set, the tool prints usage instructions and exits.

### Examples

```bash
# Explicit editor path
dotnet run --project twodog.import -- --editor /usr/bin/godot-mono ./game

# Using environment variable
export GODOT_EDITOR=/usr/bin/godot-mono
dotnet run --project twodog.import -- ./game
```

## CI/CD Integration

Set `GODOT_EDITOR` in your pipeline environment, then call the import tool before building or testing:

```yaml
# GitHub Actions example
- name: Import Godot resources
  env:
    GODOT_EDITOR: /usr/local/bin/godot-mono
  run: dotnet run --project twodog.import -- ./game

- name: Run tests
  run: dotnet test
```

## Troubleshooting

### Editor binary not found

```
Editor binary not found: /path/to/godot
```

Verify the path points to a valid Godot editor binary with Mono/.NET support. Template builds (`template_debug`, `template_release`) do not support `--import`.

### No .uid files generated

Ensure you are using an editor build of Godot, not a template build. The import pipeline is only available in editor builds with `TOOLS_ENABLED`.

### project.godot not found

```
The project path must contain a project.godot file.
```

The path argument must point to the directory containing `project.godot`, not to the file itself.
