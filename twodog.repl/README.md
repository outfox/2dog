# 2dog.repl

Reference `2dog.engine` or `2dog.engine.double` explicitly in the host project.
This integration package works with either precision.

An interactive C# prompt inside a running Godot process. Add a host with
`dnx 2dog add --repl`, then run `dotnet run --project MyGame.repl`.

Syntax highlighting, Tab/Ctrl+Space completion with documentation, signature
help, multiline input, persistent history and cancellation use Roslyn and the
package's own terminal editor. The live globals are `engine`, `tree`, `root`,
`here`, `scene`, `world`, `worlds` and `ct`. Variables and using directives persist
between submissions.

## The terminal

The input stays pinned at the bottom of the terminal, below a status line, and
everything else scrolls above it into the terminal's normal scrollback:
results, `Console` writes, `GD.Print`, Godot's errors and warnings, timers and
background tasks. An interactive host redirects the whole process's standard
output and error, including native writes, so no output can land inside the
text being typed. Godot's error and warning lines keep their colors.

Input stays live while submissions run: keep typing, or submit the next line
to queue it. A submission appears in the scrollback, with its prompt, when it
starts running, directly above its results. The status line shows running and
queued work, the session's pipe and the main editing keys. Ctrl+C clears the
draft and cancels the session's running and queued work; with a selection, it
copies instead.

Pasted text keeps its line breaks and tabs and waits for Enter. Unix terminals
mark pastes with bracketed paste; on Windows, a burst of keys that arrives while
the editor waits is treated as a paste, and a single pasted line ending in a
line break runs like a typed command. Keys typed during startup or while the
REPL is busy keep their editing behavior. The host prepares completion and
highlighting before displaying `godot>`.

Enter runs the whole input; Shift+Enter (or Alt+Enter) inserts a newline.
`:multiline` swaps Enter and Shift+Enter for the current session. Ctrl+Enter
always runs. Unix terminals report Shift+Enter through the xterm
modifyOtherKeys or kitty keyboard protocols, which the REPL requests.
Enter accepts the selected suggestion when the completion menu is open;
otherwise it keeps the selected input mode. A suggestion that is already typed
out, such as `cd $Control` with `$Control` selected, runs the input instead.
In `:auto` and `:sh`, exact command names such as `ls`, `cd`, `pwd` and `exit` run immediately
on Enter in either input mode, even with the completion menu open. This requires
no trailing whitespace; `ls ` and `cd ` keep argument completion. Enter also
accepts and runs a selected bare command, such as `l` completing to `ls`.

Suggestions open when you pause typing for a moment and list only matching items, best first, with
the selected item's documentation beside them. A second dot, as in `cd ..` or a C# range, opens none. Tab accepts a completion or opens suggestions; on a
command name such as `cd` or `ls` it adds the space and lists nodes. Further Tab presses cycle the
matches for your original prefix; Shift+Tab cycles backward and wraps around.
Backspace restores that prefix, including removing inserted parentheses; right after Tab adds a
`.`, `()` or the space after a command, it takes that back too. Escape closes the menu, which then
stays closed while you finish the word; Tab brings it back.
Shift+Backspace deletes backward to a word break, like Ctrl+Backspace;
dots and path slashes count as breaks. It also respects selections and undo.
Word breaks finish completion cycling. With the suggestion menu open, `.` or
`(` accepts a selection outside quoted path text, once you have typed part of it or chosen it with the arrows. `/` accepts a selection for
a nonempty relative path outside quoted path text; absolute-path slashes stay
separators. Inside `$["..."]`, these characters stay literal path text. Space
inserts a space and ends any existing completion cycle.
Typing more letters refines the prefix. When editing inside a node path,
completion changes only the prefix before the caret, preserving the text to
its right. Completing an exact node can insert `/` before an existing child
name. Cycling and prefix restoration preserve that suffix too.
Methods insert parentheses:
`world.Op` then Tab becomes `world.Open()` with the caret inside the call.
Parameterless calls leave the caret after `)`. Argument signatures appear
below the input, with the current parameter underlined; Ctrl+Shift+Space reopens them.
Typing `)` skips an existing closing parenthesis.

| Keys | Action |
| --- | --- |
| Up / Down | Move between lines, then browse history; typed text filters it |
| Ctrl+Z / Ctrl+Y | Undo / redo (Ctrl+Shift+Z also redoes) |
| Shift+arrows, Shift+Home/End, Ctrl+A | Select |
| Ctrl+X / Ctrl+C / Ctrl+V | Cut / copy (also to the terminal clipboard) / paste a selection |
| Ctrl+K / Ctrl+U | Delete to the end / start of the line |
| Ctrl+Left / Ctrl+Right | Move by word |
| Ctrl+L | Clear the screen |
| Ctrl+D | Exit on empty input; otherwise delete forward |

The banner, help, code and
results use color unless `NO_COLOR` is set or output is redirected.
Trees and node paths use Godot's editor family colors: green for Control,
blue for Node2D, red for Node3D, purple for Animation and neutral for other
nodes. Each path component reflects its own node type; separators are grey.
The scoped prompt uses the selected node's family color.

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

`ls` lists the selected node and its descendants (initially the whole tree).
`ls $Control/Child` lists that node and its descendants. Tab completes node paths, including after `ls `, and fills in
the exact casing. `$["Control/My Node"]` supports spaces and punctuation.
`:ls` and `:ls $Child` explicitly select the listing command in `:auto` and `:sh`.
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

`$EngineT` completes paths and searches node names across the whole live tree.
Path-prefix matches come first, followed by node-name prefixes, ignoring case.
Tab or Enter accepts a match as its complete, typed `$` path. Tab/Shift+Tab
cycles matches; Backspace restores `$EngineT`. Name matches with children end
in `/` so you can continue into the subtree; names with spaces use `$["..."]`.
This also works after `ls `, `cd ` and inside C# expressions. Matches outside
`here` insert absolute `$` paths. Search only chooses completion candidates;
evaluating a `$` path still requires an exact, case-sensitive match.
The earlier `?EngineT` spelling remains a name-search-only alias.

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

Create several named worlds with `worlds.Add("lab", "res://other.tscn")` or
`worlds.Add("empty")`. Look them up with `worlds["lab"]`, list `worlds.Names`,
and close and remove one with `worlds.Remove("lab")`. Names are unique within
each session. `world` is that session's lazy default world; closing it leaves
it available to reopen. Resetting C# preserves all worlds.

Tab completes `res://` paths in C# strings, including scene loading and the
path argument to `worlds.Add`. Scene calls offer scene files; general resource
loading offers every recognized resource. Import sidecars, hidden directories
and `.gdignore` trees are excluded. In the showcase, try
`world.Open("res://playground.tscn")`: a spinning cube and a physics ball provide
live nodes to inspect and change alongside the main demo.

## Local connections

A host running in a terminal also listens on a local named pipe under a
per-process name, and publishes itself for the `2dog` tool. Attach another
terminal with `2dog repl`. Scripts and coding agents run code with one
command per call:

```text
2dog repl exec -s agent 'cd $Main'
2dog repl exec -s agent '$Player.Position'
```

Each call prints values on stdout and compile errors, exceptions and Godot
errors on stderr. It exits with `3` when a submission fails and `124` when
`--timeout SECONDS` cancels one. A named session (`-s`) keeps its variables,
imports, mode, selected node and worlds between calls until
`2dog repl close agent` or the host stops; without `-s`, each call gets a
fresh session. `Console` and Godot output raised by a submission returns with
its result. `2dog repl sessions` lists the named sessions. See the
[`2dog repl` reference](https://2dog.dev/cli/repl).

A host started by a script needs an explicit name, which also keeps it
listening after its own input ends. Its own client connects by that name:

```text
dotnet run --project MyGame.repl -- --headless --repl-listen mygame
dotnet run --project MyGame.repl -- --repl-connect mygame
```

`--repl-connect` starts no engine. An attached terminal or piped connection
has its own C# variables, imports, mode, selected node, cancellation queue and
named worlds, which end with the connection. Submissions
run in order within a session; asynchronous submissions from different sessions
can overlap. Godot work still shares one owner thread, SceneTree and singletons.
Redirected clients use plain UTF-8 text, with complete C# submissions or multiline
blocks as input and the usual REPL results as output. Interactive clients use
the host's full editor: syntax colors, Tab completion, documentation, multiline
editing and node navigation. Completion sees the connection's own variables
and eligible `res://` resources. `NO_COLOR` disables terminal colors.
History in a connected terminal lasts for that connection.
Result values and errors return to the connection; for these connections,
`Console.WriteLine` and Godot logs still go to the host console.

`:cancel` cancels that connection's running and queued work, leaving it ready
for new input. A disconnected client cancels its work and closes its worlds.
Client EOF and `:exit` drain submitted work and disconnect without stopping the
host. The listening host stays alive after its own stdin reaches EOF; its local
`:exit` or `tree.Quit()` stops the host. The local pipe is restricted to the
current OS user. This remains a trusted, experimental execution tool.

Custom entry points use `ReplHost.Run(args, godotArgs => new Engine(...,
args: godotArgs), gameAssembly)` to enable the connection options. The original
`Run(engine, gameAssembly)` overload remains a local terminal host.

Type `:help` for examples and keyboard shortcuts, `:exit` to stop both the
prompt and the game, or `:reset` to clear C# state without restarting Godot.

Debug, Editor and Release natives are supported. Run on a JIT runtime, with
trimming, single-file publishing and NativeAOT disabled. Game assemblies must
remain on disk so Roslyn can read their metadata. Editor APIs are available
with the Editor configuration's `2dog.godotsharp.editor` reference.

The calling main thread owns Godot. Terminal editing, input and compilation run
independently of the submitted work queue. Submissions, object formatting and
normal await continuations run on the owner thread while the host pumps frames.
Ctrl+C or `:cancel` cancels running and queued work in the current session.
Submitted `while`, `do`, `for` and `foreach` loops receive cancellation
checkpoints, so an accidental `while (true) { }` can be interrupted. Pass `ct`
to asynchronous waits. Synchronous C# pauses the shared engine until it returns
or reaches a cancelled checkpoint. Blocking native/library calls, recursion,
`goto` loops and code in `#load` files are not forcibly interrupted.
Avoid `Task.Run` or `ConfigureAwait(false)` when accessing Godot objects.
Shutdown waits for running submissions and their asynchronous cleanup before
disposing Godot. Code that ignores `ct` can prevent shutdown.

Redirected input supports complete C# submissions, multiline blocks and EOF
shutdown, and returns a nonzero exit code if any submission fails.
`#r` and `#load` support local assemblies and scripts; NuGet installation from
the prompt is outside this host's scope.

Third-party license texts and source links ship in this package. See
`THIRD-PARTY-NOTICES.txt`.

## Tests and coverage

The REPL tests drive the editor with key events, render the screen through a
virtual terminal and use a live headless Godot engine. They cover persistent C# state, completion and undo, node commands,
thread affinity, cancellation, scratch worlds, output and host cleanup.
They consume the packed `2dog.repl` NuGet assembly, which carries embedded
symbols. There are no source-file, member or branch coverage exclusions.

After packing the managed packages and restoring the tests, use a fresh
results directory (replace `Debug` with `Editor` or `Release` as needed):

```text
dotnet test twodog.tests -c Debug --filter "FullyQualifiedName~Repl" --collect:"XPlat Code Coverage" --settings tests/repl/coverage.runsettings --results-directory obj/repl-coverage-check
uv run --no-project python scripts/check_repl_coverage.py obj/repl-coverage-check
```

CI runs the behavior tests on Windows, Linux and macOS in all three native
variants, and requires at least 95% line and branch coverage on Windows in
each configuration. Missing or empty coverage reports fail the check.
