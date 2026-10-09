---
title: C# REPL
description: "Explore and change a running Godot game from a terminal C# prompt with highlighting, completion and history."
---

# C# REPL

Add a terminal prompt to your Godot project, then start the game and prompt
together:

```bash
dnx 2dog add --repl
dotnet run --project MyGame.repl
```

For a new project, use `dnx 2dog new MyGame --repl`. The dotnet template also
supports `dotnet new 2dog -n MyGame --repl true --web false --tests false`.
The host is opt-in, uses `2dog.repl`, and keeps a console in every configuration.

## Explore the Running Scene

The prompt exposes `engine`, `tree`, `root`, `scene`, `world` and the
cancellation token `ct`. `Godot` and common .NET namespaces are already imported. Variables,
methods, classes and `using` directives persist between submissions. The globals
are lowercase: `tree` and `engine.Tree` refer to the live scene tree; uppercase
`Engine` resolves to Godot's static API.

Use `ls` to print the whole hierarchy or give it a `$` path to print only that
node and its descendants:

```text
ls
ls $Control
ls $Control/Player
ls $["Control/My Node"]
```

Tab completes both the command and node paths, including immediately after
`ls `. Paths keep Godot's case sensitivity; Tab inserts the exact node name.
`ls` is a REPL command and preserves C# session state. The long form is
`root.PrintTreePretty()`, a Godot `Node` method available on the root Window.

```csharp
tree
scene.GetChildren()
var node = new Node { Name = "FromRepl" }; root.AddChild(node);
node.Name
await Task.Delay(100, ct); node.Name
await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
node.QueueFree();
```

`scene` is `tree.CurrentScene`: the root node of the game's currently loaded
scene, rather than the root Window. It follows scene changes and can be null
while a change is in progress or when no main scene is loaded.

Use `$` paths to look up nodes relative to `root`. The REPL infers each node's
live public C# type, including your game's script classes, so member completion
and evaluation use that type:

```csharp
$Control.Size
$Control/Player.Health
$["Control/My Node"].Name
```

Search node names across the whole live tree with `?`, without knowing their
parents:

```text
?EngineT
ls ?EngineT
```

Tab or Enter accepts a selected match as its full `$Control/Table/EngineTimer`
path, retaining the live C# type. Tab cycles through the other matching nodes;
Shift+Tab cycles backward. Backspace restores the `?EngineT` prefix. Matching
ignores case and includes both C# and GDScript-backed nodes. Branch nodes end
in `/` so you can type a child name; names with spaces receive a quoted path.
The shortcut works inside C# expressions too. Choose a full path before
running; C# ternaries, nullable types and null-conditional access keep their
ordinary meaning.

Tab completes child paths and members. Spaces and punctuation require the
bracket form; ordinary C# interpolated strings such as `$"hello {scene.Name}"`
keep their meaning. Aliases work in REPL submissions; `#load` files use ordinary
C#. Inaccessible or generic node types fall back to a public base type.

The scaffold loads your game assembly before Godot starts, so typed lookups
use the actual live script instance. Editor bindings are available with
`-c Editor`; this starts your game with editor natives, without opening the
Godot editor interface.

Godot keeps advancing frames while you type, compile or await. Submitted code
and normal await continuations run on the thread that owns Godot. Synchronous
blocking code also blocks the game. Keep scene access on that thread; use `ct`
in waits or long loops to support cancellation, and avoid `Task.Run` and
`ConfigureAwait(false)` around Godot calls.

## Change Scenes and Open a Scratch World

Replace the game's main scene and wait for the new root to become available:

```csharp
tree.ChangeSceneToFile("res://other_scene.tscn");
await tree.ToSignal(tree, SceneTree.SignalName.SceneChanged);
scene
```

To explore a scene alongside the running game, open the REPL's scratch window:

```csharp
world.Open("res://other_scene.tscn");
world.Scene.GetChildren()
world.Viewport
world.Clear(); // replace the scratch scene with an empty node
world.Close();
```

The scratch viewport has independent 2D and 3D worlds. Opening, clearing or
closing it leaves the game's main scene running. It shares the SceneTree,
autoloads and singletons with the game, so scene code can still affect those.
In headless mode it runs without displaying a window. `:reset` clears C#
variables and imports while preserving both scenes.

## Terminal Controls

| Key or command | Action |
| --- | --- |
| Tab | Accept a completion, then cycle matching choices forward |
| Shift+Tab | Cycle matching choices backward, wrapping at the ends |
| Backspace after completion | Restore the prefix you typed |
| Ctrl+Space | Open completion suggestions |
| Ctrl+Shift+Space | Show method signatures and argument help |
| Enter | Accept the selected completion; otherwise run the input |
| Ctrl+Enter | Run the entire input immediately |
| Shift+Enter, Alt+Enter | Insert a newline |
| `:multiline` | Swap Enter and Shift+Enter for this session |
| Up, Down | Browse history, persisted between runs |
| Ctrl+C | Cancel input or cooperatively cancel the running submission |
| Ctrl+L, `:clear` | Clear the screen |
| Ctrl+D on empty input, `:quit` | Stop the prompt and game |
| `:help` | Show examples, globals and keybindings |
| `:reset` | Clear C# session variables and imports, preserving the scene |

Completing `world.Op` with Tab inserts `world.Open()` and places the caret
inside the parentheses for arguments. Methods without parameters leave the
caret after `)`. Existing argument lists are preserved, and typing `)` skips
an existing closing parenthesis. Signatures and parameter documentation appear
while you fill in a call; Up and Down select overloads when that menu is active.

After accepting a completion, Tab and Shift+Tab cycle the matches for the
prefix you originally typed, wrapping at the ends. Backspace restores that
prefix and removes any automatically inserted parentheses. Typing a word break
such as `.`, `/`, a space or `(` accepts the current choice and ends cycling.
Typing more letters refines the prefix. Type `.` to inspect members of an
object such as `world.Scene`, or `/` to explore children of an unquoted `$` path.
Quoted paths use `.` to continue into C# members.

Startup shows `Preparing C# completion...` while language services initialize.
The `godot>` prompt appears when they are ready. Keys typed during startup
remain queued; rapid typing preserves Enter, Tab and other controls. Unix
terminals with bracketed paste support keep pasted multiline code together as
one edit. Windows' normal console key API removes those paste boundaries;
use `:multiline` before pasting several lines, then Ctrl+Enter to run them.

When the completion menu has a selection, Enter accepts it without running
the input. Otherwise Enter runs input by default. Use `:multiline` to make Enter
insert a newline and Shift+Enter run instead. Use `:multiline` again to restore the default.
Ctrl+Enter always runs; changing the input mode preserves your C# variables,
scenes and history.

The terminal uses [PrettyPrompt](https://github.com/waf/PrettyPrompt) and
[Roslyn](https://github.com/dotnet/roslyn), following the architecture of
[CSharpRepl](https://github.com/waf/CSharpRepl). Completion includes Godot,
game types, live node paths and previous submissions, with documentation
tooltips. The banner, help and results also use colors and spacing. Set
`NO_COLOR` to disable colors. Local `#r "assembly.dll"` and
`#load "script.csx"` directives are supported. Installing NuGet packages from
the prompt and attaching to other running processes are outside this host.

Redirected input also works: send complete submissions on stdin and close the
stream to stop. Compilation or runtime failures print diagnostics and result
in a nonzero exit code. See [REPL configuration](/configuration/repl) for
variants and publishing.

The repository's showcase runs its scene probes quietly by default. To print
their success markers for smoke checks, pass the Godot user argument
`--2dog-smoke-markers` after a second `--`:

```bash
dotnet run --project demos/showcase/showcase.repl -- -- --2dog-smoke-markers
```
