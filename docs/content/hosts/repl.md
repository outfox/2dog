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

C# has access to `engine`, `tree`, `root`, `scene`, `here`, the default scratch
world `world`, named `worlds`, and the cancellation token `ct`. Godot and common .NET namespaces
are already imported. For example:

```csharp
var node = new Node { Name = "FromRepl" };
root.AddChild(node);
node.Name
node.QueueFree();
```

The game keeps running while you type or await. Input and editing remain
available while submissions run. Use Ctrl+C or `:cancel` to cancel running and
queued work. Submitted C# loops receive cancellation checkpoints; pass `ct`
to long asynchronous waits. Blocking library or native calls still need their
own cancellation support. Keep Godot calls on the engine's owner thread.

Create several named scratch worlds alongside the game:

```csharp
worlds.Add("lab", "res://other.tscn");
worlds.Add("empty");
worlds["lab"].Scene.GetChildren()
worlds.Names
worlds.Remove("empty");
```

`world` is your session's default world. Each world has its own viewport and
2D/3D physics worlds, while sharing the SceneTree and singletons. Tab completes
eligible `res://` paths inside resource strings. The showcase includes
`res://playground.tscn` with a spinning cube and a physics ball to experiment on.

For multiple terminals or agent processes, start with `--repl-listen mygame`
and connect with `--repl-connect mygame`. Each connection gets independent C#
state and scratch worlds. See [local connections](/configuration/repl#local-connections)
for piping examples and lifecycle details.

See [REPL configuration](/configuration/repl) for native variants, headless
use and publishing.
