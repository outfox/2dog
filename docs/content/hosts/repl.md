---
title: C# REPL
description: "Explore and change a running Godot game with C# and shell-like commands."
tagline: "Learn how to explore and change a running game live, with C# and shell-like commands."
keywords: "Godot REPL, C# REPL, live Godot scripting"
---

# C# REPL

Add an interactive C# prompt to your game from its Godot project directory.

![Illus. Diego Rago: a white anthro dog in a hacker hoodie playing tug of war with of their robot dog](../img/2dog-tugofwar.png)
*"Be a nice dogbot. Share that assembly load context!"*

::: danger Don't play too rough! :stop_sign:
Give access only to trusted users. Do not call methods that can break the game,
such as `tree.free()`.

This is an experimental, powerful new way to interact with your Godot project or the editor *while it's running!*
:::

Create a REPL host:

```bash
dnx 2dog add --repl
dotnet run --project MyGame.repl
```

For a new project with only a REPL host, use `dnx 2dog new MyGame --repl`.

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
to a child of the selected node. The expression has the live C# type of that
child.

Use `engine`, `tree`, `root`, `scene` and `here` to access the running game.
Godot and common .NET namespaces are already imported:

```csharp
var node = new Node { Name = "FromRepl" };
root.AddChild(node);
node.Name
node.QueueFree();
```

| Mode              | Input                                                               |
|-------------------|---------------------------------------------------------------------|
| `:auto` (default) | C# and shell-like commands                                          |
| `:sh`             | Shell-like commands, with C# expressions for properties and methods |
| `:cs`             | C# statements, declarations and expressions                         |

When you switch modes, you keep your variables and the selected node. Type
`:help` to see the commands and keybindings. Type `:exit` to stop the prompt
and the game.

- Tab completes commands, node paths and C# members.
- Shift+Enter adds a line.
- Up and Down browse the history.
- Ctrl+C or `:cancel` cancels the running work and the queued work.

Output scrolls above your input. New submissions wait in a queue while earlier
ones run. Pass `ct` to asynchronous waits so that Ctrl+C can cancel them.
Ctrl+C cannot stop a blocking call unless that call supports cancellation.
Call Godot APIs only on the thread that owns the engine.

## Scratch Worlds

`world` is your session's default scratch world. Create others with `worlds`:

```csharp
worlds.Add("lab", "res://other.tscn");
worlds["lab"].Scene.GetChildren()
worlds.Remove("lab");
```

Each world has its own viewport and 2D/3D physics. All worlds share the
SceneTree and the singletons. To create an empty world, omit the scene path.

## Coding Agents

Start the host so that it listens for commands:

```bash
dotnet run --project MyGame.repl -- --repl-listen mygame
```

Then run commands from another terminal or agent:

```bash
2dog repl exec -s agent --timeout 30 'cd scene'
2dog repl exec -s agent --timeout 30 'here.GetChildren()'
```

The named session keeps variables, node selection and worlds between calls.
Sessions share the live game; use scratch worlds for separate experiments.
Each call returns output and an exit code. See [`2dog repl`](/cli/repl) under
Develop for options, sessions and terminal attachment.

[`2dog skill`](/cli/skill) installs the REPL guide for Claude Code. To write
the guide into `AGENTS.md` instead, add `--codex`. To show the guide at the
prompt, type `:help agents`.

See [REPL configuration](/configuration/repl) for native variants, headless
use, local connections and publishing.
