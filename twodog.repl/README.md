# 2dog.repl

An interactive C# prompt inside a running Godot process. Add a host with
`dnx 2dog add --repl`, then run `dotnet run --project MyGame.repl`.

Syntax highlighting, Tab/Ctrl+Space completion with documentation, multiline
input, persistent history and cancellation use PrettyPrompt and Roslyn, the
libraries underlying CSharpRepl. The live globals are `engine`, `tree`, `root`,
`scene`, `world` and `ct`. Variables and using directives persist between submissions.
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
Tab accepts a completion or opens suggestions. Further Tab presses cycle the
matches for your original prefix; Shift+Tab cycles backward and wraps around.
Backspace restores that prefix, including removing inserted parentheses.
Typing a word break such as `.`, `/`, a space or `(` accepts the current choice.
Typing more letters refines the prefix. Methods insert parentheses:
`world.Op` then Tab becomes `world.Open()` with the caret inside the call.
Parameterless calls leave the caret after `)`. Argument signatures appear
automatically; Ctrl+Shift+Space reopens them.
Typing `)` skips an existing closing parenthesis. The banner, help, code and
results use color unless `NO_COLOR` is set or output is redirected.

`ls` lists the whole scene hierarchy; `ls $Control/Child` lists that node and
its descendants. Tab completes node paths, including after `ls `, and fills in
the exact casing. `$["Control/My Node"]` supports spaces and punctuation.
Listing preserves your C# variables and imports.

`?EngineT` searches node names anywhere in the live tree, ignoring case.
Tab or Enter accepts a match as its complete, typed `$` path. Tab/Shift+Tab
cycles matches; Backspace restores `?EngineT`. Branches end in `/` so you can
type a child name; names with spaces use the quoted `$["..."]` form. This also
works after `ls ` and inside C# expressions. The search is a completion shortcut;
choose a full path before running. Ordinary C# `?`, `?.` and `??` stay C#.

`scene` follows `tree.CurrentScene`, the root node of the game's loaded scene.
Use `tree.ChangeSceneToFile("res://other.tscn")` to replace it, then await
`tree.ToSignal(tree, SceneTree.SignalName.SceneChanged)` before accessing it.
`$Control/Child` looks up a path relative to `root` and infers its live public
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
