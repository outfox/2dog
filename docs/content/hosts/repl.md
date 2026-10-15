---
title: C# REPL
description: "Explore and change a running Godot game with C# and node shell commands."
---

# C# REPL

Add an interactive C# prompt to your game from its Godot project directory:

```bash
dnx 2dog add --repl
dotnet run --project MyGame.repl
```

For a new project, use `dnx 2dog new MyGame --repl`.

## Try It

Explore the live scene tree:

```text
ls
cd scene
ls
here.Name
cd /
```

`ls` lists nodes, `cd` selects one, and `pwd` shows its path. `$Child` refers
to a child of the selected node, using its live C# type.

Use `engine`, `tree`, `root`, `scene` and `here` to access the running game.
Godot and common .NET namespaces are already imported:

```csharp
var node = new Node { Name = "FromRepl" };
root.AddChild(node);
node.Name
node.QueueFree();
```

| Mode | Input |
| --- | --- |
| `:auto` (default) | C# and node shell commands |
| `:sh` | Node shell commands, with C# expressions for properties and methods |
| `:cs` | C# statements, declarations and expressions |

Variables and the selected node carry over when switching modes. Type `:help`
for commands and keybindings, or `:exit` to stop the prompt and game.

Tab completes commands, node paths and C# members. Shift+Enter adds a line;
Up/Down browse history. Output scrolls above your input, and new submissions
queue while earlier ones run. Ctrl+C or `:cancel` cancels running and queued
work. Pass `ct` to asynchronous waits; blocking calls need cancellation
support of their own. Keep Godot calls on the engine's owner thread.

## Scratch Worlds

`world` is your session's default scratch world. Create others with `worlds`:

```csharp
worlds.Add("lab", "res://other.tscn");
worlds["lab"].Scene.GetChildren()
worlds.Remove("lab");
```

Each world has its own viewport and 2D/3D physics, sharing the SceneTree and
singletons. Omit the scene path to create an empty world.

## Coding Agents

Start a listening host, then run commands from another terminal or agent:

```bash
dotnet run --project MyGame.repl -- --repl-listen mygame
```

```bash
2dog repl exec -s agent --timeout 30 'cd scene'
2dog repl exec -s agent --timeout 30 'here.GetChildren()'
```

The named session keeps variables, node selection and worlds between calls.
Sessions share the live game; use scratch worlds for separate experiments.
Each call returns output and an exit code. See [`2dog repl`](/cli/repl) under
Develop for options, sessions and terminal attachment.

[`2dog skill`](/cli/skill) installs the working guide for Claude Code; add
`--codex` to write it into `AGENTS.md`. The prompt also shows it with
`:help agents`.

See [REPL configuration](/configuration/repl) for native variants, headless
use, local connections and publishing.
