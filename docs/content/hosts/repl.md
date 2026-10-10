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
supports `dotnet new 2dog -n MyGame --repl true --web false`.
The host is opt-in, uses `2dog.repl`, and keeps a console in every configuration.

## Modes

Use `:auto`, `:sh`, `:cs` or `:ai` to change how submissions are interpreted.
Switching modes preserves C# variables, imports and the selected node.

| Mode | Behavior |
| --- | --- |
| `:auto` (default) | C# and node shell commands together, with the existing command disambiguation rules |
| `:sh` | Shell command names take priority, even when a C# variable has the same name; single expressions inspect properties and call node methods |
| `:cs` | Pure C# submissions: no shell commands; typed `$` paths, `?` searches and Tab completion still work |
| `:ai` | Agent stub: each submission replies "the lights are on, but there's no one home" and executes no code |

The prompt shows `godot:sh>`, `godot:cs>` or `godot:ai>` outside the default
mode, alongside the selected node path. `:help`, `:exit`, `:clear`,
`:multiline`, `:reset` and mode switches work in every mode. `:reset` clears
C# variables while preserving the mode and node scope. Bare `exit` quits in
`:auto` and `:sh`; use `:exit` in the other modes.

```text
:cs
var mv = 42;
mv
:sh
cd $Control
here.Name
$CenterContainer.Show()
:auto
```

Shell mode accepts node commands and single C# expressions, including node
properties and method calls. Use `:cs` for declarations, multiple statements,
`using` directives or loading scripts. In `:cs`, names such as `ls` and `mv`
are ordinary C# identifiers; colon-prefixed shell commands are also disabled.

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

Use `$` paths to look up nodes relative to the selected node (`here`), which
starts at `root`. The REPL infers each node's
live public C# type, including your game's script classes, so member completion
and evaluation use that type:

```csharp
$Control.Size
$Control/Player.Health
$["Control/My Node"].Name
```

Navigate the live tree without losing C# variables or imports:

```text
cd $Control/Signals/Table/
ls
$EngineTimerName
pwd
cd ..
cd /
```

`cd` selects a node. `ls` prints that node and its descendants, and `$Child`
paths and completion start there. `here` exposes the selected node to C#;
`root` always refers to the engine's root Window. The prompt shows your current
path (shortened in narrow terminals; `pwd` always prints it in full).
`cd ..` selects the parent, while `cd /` or bare `cd` returns to root.
Use `cd $/root/Control` or `:cd /root/Control` for absolute paths. Quoted paths
such as `cd $["My Node"]` support spaces and punctuation.

You can also select a C# node variable or expression:

```text
cd /
var x = $Control/CenterContainer;
cd x
cd x.GetParent()
cd scene
:cd (scene ?? root)
```

Tab completes variables and members in the argument. The expression runs once
on the Godot thread and must return a live node in this engine's scene tree.
Null, freed or detached nodes and other values produce an error and preserve
the selected scope. Existing variables, imports and expression side effects
remain available; navigation itself does not add a C# submission.
Use `:cd` for a parenthesized expression, since `cd (node)` is a C# call.

No colon is required for these navigation forms. C# calls, assignments and
arithmetic using `cd` or `pwd` keep their normal meaning. If you declare a C#
variable named `cd` or `pwd`, its bare name evaluates that variable; use `:cd`
or `:pwd` to explicitly run the command instead. The scope follows renaming and
reparenting. If the selected node is freed, queued for deletion or removed from
the tree, it falls back to root. `:reset` preserves the selected scope.

Remove a node and its descendants with `rm`:

```text
rm $Child
rm $["My Node"]
rm savedNode
rm savedNode.GetChild(0)
```

Paths are relative to `here`; absolute paths, C# node expressions, `?` search
and Tab completion work as they do for `cd`. `:rm` is the explicit command
form. A target is required, and the engine's root Window cannot be removed.
Removal uses Godot's deferred `QueueFree()` on the owner thread. Removing the
selected node or an ancestor returns the scope to root. C# variables persist,
but saved references to removed nodes are no longer valid after deletion.

`cp $Source $Parent` duplicates a node and its descendants into an existing
parent. `mv $Source $Parent` reparents the original, preserving its global
transform where Godot supports it (Control preserves position). Both accept
C# node variables and expressions in either argument, with Tab completion:

```text
cp $Player $Backup
mv savedNode destination
mv $Player "Hero"
:cp (source ?? here) (destination ?? root)
```

Arguments are two whitespace-separated C# expressions; use parentheses to
make expression boundaries clear. Both run once, left to right, on the Godot
thread. The root Window, self/descendant destinations and conflicting child
names are rejected. `mv $Old "NewName"` renames a node in place; a C#
string expression also works as the new name. Invalid names and sibling
collisions are rejected before changing the tree. Renaming preserves the node
identity and updates the selected scope and completion paths. `cp` uses Godot's
standard `Duplicate()` behavior: serialized properties, children, groups,
signals and scripts; resources retain their normal sharing rules. Internal
nodes and arbitrary runtime fields are not copied. These commands change the
live tree; they do not save scene files. Moving `here` preserves its identity
and updates the prompt path.

In `:auto`, command names remain usable in C#: `mv(...)`, `mv.Member`, `mv[index]`,
assignments and arithmetic keep their C# meaning, even with whitespace before
the punctuation. `mv source parent` is a command even if `mv` is a variable;
`:mv` explicitly forces the command. `@mv` explicitly selects the C# identifier.
As with `cd`, a bare `mv` or `cp` evaluates an existing variable of that name.

Use `$` to complete a path or find a node without knowing its parents:

```text
$EngineT
ls $EngineT
cd $EngineT
```

Path-prefix matches come first, followed by node-name prefixes anywhere in the
live tree. Both ignore case, preferring exact case within each group. Tab or
Enter accepts a selected match as its full `$Control/Table/EngineTimer` path,
retaining the live C# type. Matches outside the selected scope insert an
absolute path such as `$/root/Control/Table/EngineTimer`. Tab cycles through
matching nodes; Shift+Tab cycles backward. Backspace restores the `$EngineT`
prefix. Name matches with children end in `/` so you can continue into the
subtree; names with spaces receive a quoted path. This works with both C# and
GDScript-backed nodes and inside C# expressions.

Searching affects completion only. Evaluating a `$` path still requires an
exact, case-sensitive match. The earlier `?EngineT` spelling remains a
name-search-only alias; ordinary C# `?`, `?.` and `??` keep their meaning.

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
Shutdown waits for running submissions and their asynchronous cleanup before
disposing Godot. Code that ignores `ct` can prevent shutdown.

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
| Shift+Backspace, Ctrl+Backspace | Delete backward to a word break, including dots and slashes |
| Ctrl+Space | Open completion suggestions |
| Ctrl+Shift+Space | Show method signatures and argument help |
| Enter | Accept the selected completion; otherwise run the input |
| Ctrl+Enter | Run the entire input immediately |
| Shift+Enter, Alt+Enter | Insert a newline |
| `:multiline` | Swap Enter and Shift+Enter for this session |
| Up, Down | Browse history, persisted between runs |
| Ctrl+C | Cancel input or cooperatively cancel the running submission |
| Ctrl+L, `:clear` | Clear the screen |
| Ctrl+D on empty input, `:exit` | Stop the prompt and game |
| `cd $Child`, `cd nodeExpression`, `cd ..`, `cd /` | Select a node, its parent or the root |
| `pwd`, `:pwd` | Show the selected node's absolute path |
| `rm $Child`, `rm nodeExpression` | Remove a scene node and its descendants |
| `:help` | Show examples, globals and keybindings |
| `:reset` | Clear C# session variables and imports, preserving the scene |

Completing `world.Op` with Tab inserts `world.Open()` and places the caret
inside the parentheses for arguments. Methods without parameters leave the
caret after `)`. Existing argument lists are preserved, and typing `)` skips
an existing closing parenthesis. Signatures and parameter documentation appear
while you fill in a call; Up and Down select overloads when that menu is active.

After accepting a completion, Tab and Shift+Tab cycle the matches for the
prefix you originally typed, wrapping at the ends. Backspace restores that
prefix and removes any automatically inserted parentheses. Word breaks finish
completion cycling. With the suggestion menu open, `.` or `(` accepts a
selection outside quoted path text. `/` accepts a selection for a nonempty
relative path outside quoted path text; absolute-path slashes stay separators.
Inside `$["..."]`, these characters stay literal path text. Space inserts a
space and ends any existing completion cycle.
Typing more letters refines the prefix. Type `.` to inspect members of an
object such as `world.Scene`, or `/` to explore children of an unquoted `$` path.
Quoted paths use `.` to continue into C# members.

When editing inside a node path, completion uses the prefix before the cursor
and preserves the text to its right. For example, completing `ls $Cont|Flair`
produces `ls $Control|Flair`, where `|` marks the cursor. If the node is already
complete, Tab can insert `/` before the existing child name. Tab cycling and
Backspace restoration also keep the suffix intact, including in quoted paths.

Startup shows `Preparing C# completion...` while language services initialize.
The `godot>` prompt appears when they are ready. Keys typed during startup
remain queued; rapid typing preserves Enter, Tab and other controls. Unix
terminals with bracketed paste support keep pasted multiline code together as
one edit. Windows' normal console key API removes those paste boundaries;
use `:multiline` before pasting several lines, then Ctrl+Enter to run them.

When the completion menu has a selection, Enter accepts it without running
the input. In `:auto` and `:sh`, exact command names such as `ls`, `cd`,
`pwd` and `exit` run immediately on Enter in either input mode, even with a suggestion selected.
This exception requires no trailing whitespace: `ls ` and `cd ` keep argument
completion. Enter also accepts and runs a selected bare command, such as `l`
completing to `ls`. Otherwise Enter runs input by default. Use `:multiline` to make Enter
insert a newline and Shift+Enter run instead. Use `:multiline` again to restore the default.
Ctrl+Enter always runs; changing the input mode preserves your C# variables,
scenes and history.

The terminal uses [PrettyPrompt](https://github.com/waf/PrettyPrompt) and
[Roslyn](https://github.com/dotnet/roslyn), following the architecture of
[CSharpRepl](https://github.com/waf/CSharpRepl). Completion includes Godot,
game types, live node paths and previous submissions, with documentation
tooltips. The banner, help and results also use colors and spacing. Trees, path
completion and input use Godot's editor node family colors: light green for
Control, light blue for Node2D, light red for Node3D and magenta for Animation.
Custom script classes inherit their native node's color. Each path component
uses its node's family, with grey slashes and tree connectors. The scoped
prompt follows the selected node's family. Set
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
