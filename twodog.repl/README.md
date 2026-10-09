# 2dog.repl

An interactive C# prompt inside a running Godot process. Add a host with
`dnx 2dog add --repl`, then run `dotnet run --project MyGame.repl`.

Syntax highlighting, Tab/Ctrl+Space completion with documentation, multiline
input, persistent history and cancellation use PrettyPrompt and Roslyn, the
libraries underlying CSharpRepl. The live globals are `engine`, `tree`, `root`,
`here`, `scene`, `world` and `ct`. Variables and using directives persist
between submissions.
The host prepares completion and highlighting before displaying `godot>`.
Keys typed during startup remain queued, and buffered Enter/Tab/Escape keys
keep their editing behavior rather than becoming pasted text. Unix terminals
with bracketed paste support keep pasted multiline code in a single edit.
For Windows terminal paste, use `:multiline` before pasting several lines,
then Ctrl+Enter to run them.
Enter runs the whole input; Shift+Enter inserts a newline. `:multiline`
toggles those two keys for the current session. Ctrl+Enter always runs.
Enter accepts the selected suggestion when the completion menu is open;
otherwise it keeps the selected input mode. Ctrl+Enter always runs immediately.
Exact command names such as `ls`, `cd`, `pwd`, `exit` and `:help` run immediately
on Enter in either input mode, even with the completion menu open. This requires
no trailing whitespace; `ls ` and `cd ` keep argument completion. Enter also
accepts and runs a selected bare command, such as `l` completing to `ls`.
Tab accepts a completion or opens suggestions. Further Tab presses cycle the
matches for your original prefix; Shift+Tab cycles backward and wraps around.
Backspace restores that prefix, including removing inserted parentheses.
Shift+Backspace deletes backward to a word break, like Ctrl+Backspace;
dots and path slashes count as breaks. It also respects selections and undo.
Typing a word break such as `.`, `/`, a space or `(` accepts the current choice.
Typing more letters refines the prefix. When editing inside a node path,
completion changes only the prefix before the caret, preserving the text to
its right. Completing an exact node can insert `/` before an existing child
name. Cycling and prefix restoration preserve that suffix too.
Methods insert parentheses:
`world.Op` then Tab becomes `world.Open()` with the caret inside the call.
Parameterless calls leave the caret after `)`. Argument signatures appear
automatically; Ctrl+Shift+Space reopens them.
Typing `)` skips an existing closing parenthesis. The banner, help, code and
results use color unless `NO_COLOR` is set or output is redirected.
Trees and node paths use Godot's editor family colors: green for Control,
blue for Node2D, red for Node3D, purple for Animation and neutral for other
nodes. Each path component reflects its own node type; separators are grey.
The scoped prompt uses the selected node's family color.

`ls` lists the selected node and its descendants (initially the whole tree).
`ls $Control/Child` lists that node and its descendants. Tab completes node paths, including after `ls `, and fills in
the exact casing. `$["Control/My Node"]` supports spaces and punctuation.
`cd $Control/Signals/Table/` selects a node: subsequent `ls` and `$Child`
paths start there. `here` is that node, `pwd` prints its absolute path, and the
prompt shows it. `cd ..` selects the parent; `cd /` or `cd` returns to `root`.
`root` always remains the engine's root Window. Use `cd $/root/Control` for
absolute paths, or `:cd /root/Control`. `:cd` and `:pwd` force command handling
when C# variables share those names; calls, assignments and arithmetic stay C#.
Node variables and expressions work too: `var x = $Control/CenterContainer;`
then `cd x`, `cd x.GetParent()` or `cd scene`. Tab completes C# variables and
members in the argument. Use `:cd (scene ?? root)` for parentheses, since
`cd (node)` stays an ordinary C# call. Expressions run once on the Godot thread
and must return a live node in this engine's scene tree. A failed expression
keeps the selected scope. Navigation does not add a C# submission; existing
variables, imports and expression side effects remain available.
The scope follows a renamed or reparented node, and returns to root if that node
is freed or leaves the tree. Navigation preserves C# variables and imports;
`:reset` preserves the scope.

`rm $Child` removes a node and its descendants with Godot's deferred
`QueueFree()`. `rm savedNode`, `rm savedNode.GetChild(0)` and `:rm` forms
accept the same node expressions and completion as `cd`. A target is required;
the engine's root Window is protected. Removing the selected node or one of
its ancestors returns the scope to root. C# variables persist, but a saved
reference to a removed node is no longer valid after deletion.

`cp $Source $Parent` duplicates a node and its descendants into an existing
parent. `mv $Source $Parent` reparents the original, preserving its global
transform where Godot supports it (Control preserves position). Both accept
C# node variables and expressions in either argument, with Tab completion:

```text
cp $Player $Backup
mv savedNode destination
:cp (source ?? here) (destination ?? root)
```

Arguments are two whitespace-separated C# expressions; use parentheses to
make expression boundaries clear. Both run once, left to right, on the Godot
thread. The root Window, self/descendant destinations and conflicting child
names are rejected. Rename nodes through C# when needed. `cp` uses Godot's
standard `Duplicate()` behavior: serialized properties, children, groups,
signals and scripts; resources retain their normal sharing rules. Internal
nodes and arbitrary runtime fields are not copied. These commands change the
live tree; they do not save scene files. Moving `here` preserves its identity
and updates the prompt path.

Command names remain usable in C#: `mv(...)`, `mv.Member`, `mv[index]`,
assignments and arithmetic keep their C# meaning, even with whitespace before
the punctuation. `mv source parent` is a command even if `mv` is a variable;
`:mv` explicitly forces the command. `@mv` explicitly selects the C# identifier.
As with `cd`, a bare `mv` or `cp` evaluates an existing variable of that name.

`?EngineT` searches node names anywhere in the live tree, ignoring case.
Tab or Enter accepts a match as its complete, typed `$` path. Tab/Shift+Tab
cycles matches; Backspace restores `?EngineT`. Branches end in `/` so you can
type a child name; names with spaces use the quoted `$["..."]` form. This also
works after `ls `, `cd ` and inside C# expressions. Matches outside your scope
insert absolute `$` paths. The search is a completion shortcut;
choose a full path before running. Ordinary C# `?`, `?.` and `??` stay C#.

`scene` follows `tree.CurrentScene`, the root node of the game's loaded scene.
Use `tree.ChangeSceneToFile("res://other.tscn")` to replace it, then await
`tree.ToSignal(tree, SceneTree.SignalName.SceneChanged)` before accessing it.
`$Child` looks up a path relative to `here` and infers its live public
C# type for member completion and evaluation. `$["Control/My Node"]` supports
spaces and punctuation. These aliases work in submissions; `#load` files use
ordinary C#.

`world.Open("res://other.tscn")` opens a scratch window alongside the game,
with independent 2D and 3D worlds. Inspect `world.Scene` or `world.Viewport`,
use `world.Clear()` for an empty scene and `world.Close()` to close it.
The SceneTree, autoloads and singletons are shared with the game.

Type `:help` for examples and keyboard shortcuts, `:quit` to stop both the
prompt and the game, or `:reset` to clear C# state without restarting Godot.

Debug, Editor and Release natives are supported. Run on a JIT runtime, with
trimming, single-file publishing and NativeAOT disabled. Game assemblies must
remain on disk so Roslyn can read their metadata. Editor APIs are available
with the Editor configuration's `2dog.godotsharp.editor` reference.

The calling main thread owns Godot. Terminal editing and compilation run in
the background; submissions, object formatting and normal await continuations
run on the owner thread while the host pumps frames. Synchronous blocking C#
blocks the game too. Cancellation is cooperative: use `ct` in waits and loops.
Avoid `Task.Run` or `ConfigureAwait(false)` when accessing Godot objects.

Redirected input supports complete C# submissions, multiline blocks and EOF
shutdown, and returns a nonzero exit code if any submission fails.
`#r` and `#load` support local assemblies and scripts; NuGet installation from
the prompt and attaching to other processes are outside this host's scope.

Third-party license texts and source links ship in this package. See
`THIRD-PARTY-NOTICES.txt`.
