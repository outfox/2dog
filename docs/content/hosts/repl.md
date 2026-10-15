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

## The Terminal

Your input stays at the bottom of the terminal, below a status line. Results,
`GD.Print`, Godot's errors and warnings, timers and other output scroll above
it into the terminal's normal scrollback, so they never land in the line you
are typing. Submit the next line while one runs to queue it; each submission
appears in the scrollback when it starts, directly above its results, and the
status line counts running and queued work.

Suggestions open when you pause typing, with documentation for the selected item and
signatures for the call you are in. Pasted code keeps its line breaks until you
press Enter; Shift+Enter adds a line and Up/Down browse history.

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

## Work Together

The running game is shared: other terminals and coding agents can join it
while you type. Attach another terminal with `2dog repl`. Scripts and agents
run code with [`2dog repl exec`](/cli/repl), one shell command per step:

```bash
2dog repl exec -s agent 'cd $Main'
2dog repl exec -s agent '$Player.Position += Vector3.Up; $Player.Position'
```

Each call prints its result and exits with a status, so it works from any
shell tool. The named session `agent` keeps its variables, selected node and
worlds between calls. Everyone edits the same live scene tree; use
`worlds.Add` to experiment without touching the game. See
[local connections](/configuration/repl#local-connections) for how hosts are
found and how long sessions live.

See [REPL configuration](/configuration/repl) for native variants, headless
use and publishing.
