---
title: C# REPL
description: "Explore and change a running Godot game from an interactive C# and node shell prompt."
---

# C# REPL

Run your game with an interactive terminal prompt. Add a REPL host from your
Godot project directory:

```bash
dnx 2dog add --repl
dotnet run --project MyGame.repl
```

For a new project, use `dnx 2dog new MyGame --repl`.

Type `:help` for the current commands, examples and keybindings. The REPL is
still evolving; the built-in help is the best place to explore its controls.
Use `:exit` to stop the prompt and game.

## Modes

There are currently three usable modes. Type a mode command to switch;
your C# variables and selected node carry over.

| Mode | What it does |
| --- | --- |
| `:auto` (default) | Mix C# with node shell commands. |
| `:sh` | Navigate and manipulate nodes with shell commands; inspect properties and call methods with C# expressions. |
| `:cs` | Write C# statements, declarations and expressions, without shell commands. |

`:ai` is a placeholder for a future agent mode.

## Try It

In the default mode, explore the live scene tree:

```text
ls
cd scene
ls
here.Name
cd /
```

`ls` lists nodes, `cd` selects one, and `pwd` shows its path. Use `$Child`
to refer to a node relative to your selection. Tab completes commands,
node paths and C# members.

C# has access to `engine`, `tree`, `root`, `scene`, `here`, the scratch world
`world`, and the cancellation token `ct`. Godot and common .NET namespaces
are already imported. For example:

```csharp
var node = new Node { Name = "FromRepl" };
root.AddChild(node);
node.Name
node.QueueFree();
```

The game keeps running while you type or await. Code runs on Godot's owner
thread, so blocking code pauses the game. Keep Godot calls on that thread
and use `ct` to make long waits cancellable.

See [REPL configuration](/configuration/repl) for native variants, headless
use and publishing.
