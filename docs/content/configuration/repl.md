---
title: REPL Configuration
description: "Run the REPL with different Godot native variants, headless mode or a regular .NET publish."
---

# REPL Configuration

The generated `MyGame.repl/` host references your game and `2dog.repl`.
Choose the Godot native variant with the build configuration:

| Configuration | Native variant |
| --- | --- |
| Debug (default) | `debug` |
| Editor | `editor` |
| Release | `release` |

```bash
dotnet run --project MyGame.repl -c Editor
dotnet run --project MyGame.repl -- --headless
```

Editor runs the game with editor natives; it does not open the Godot editor.
Arguments after `--` go to Godot. The shared
[engine properties](/configuration#properties) also apply.

## Publishing

The REPL compiles C# at runtime. Use a regular folder publish; `PublishAot`,
`PublishTrimmed` and `PublishSingleFile` must be `false`.

```bash
dotnet publish MyGame.repl -c Release
```

See [REPL](/hosts/repl) to get started, and type `:help` in the prompt for
commands and controls.
