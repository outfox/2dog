# Working in a live 2dog game

A 2dog REPL host is a running Godot game with a C# prompt inside it. You change
the live scene tree from the shell with `2dog repl exec`, one command at a time,
and see the result at once. Other people and agents may share the same game.

## Connect

```bash
2dog repl sessions                                   # is a host running? who else is here?
2dog repl exec -s NAME --timeout 30 '$Player.Position'
2dog repl exec -s NAME --timeout 30 - <<'EOF'
var start = $Player.Position;
$Player.Position += Vector2.Right * 10;
$Player.Position - start
EOF
```

- Pick one session name and keep it: `-s NAME` keeps your variables, selected
  node and worlds between calls. Without `-s`, every call starts fresh.
- Always pass `--timeout`. Exit codes: `0` ok, `1` bad command line, `2` no host
  or the connection broke, `3` a submission failed, `124` timed out.
- Add `--strict` to fail on Godot errors and warnings, and `--json` for a
  structured report (values, output, Godot log, and listings as trees).
- `--pipe NAME` chooses a host when several are running.
- Code that starts with `-` goes after `--`: `2dog repl exec -- -1 + 2`.

## The globals

| Name | What it is |
| --- | --- |
| `tree` | The `SceneTree`. There is no `GetTree()` at the top level. |
| `root` | The root `Window`. |
| `scene` | The current game scene. |
| `here` | The node selected with `cd`; `$` paths start here. |
| `world`, `worlds` | Scratch worlds in windows of their own, beside the game. |
| `ct` | Cancellation for the running submission; pass it to waits. |
| `engine` | The 2dog `Engine`. |

`System`, `System.Linq`, `System.Collections.Generic`, `System.Threading`,
`System.Threading.Tasks` and `Godot` are imported. `Timer` means `Godot.Timer`.

## Nodes

- `$Player` is the node `Player` below `here`, typed as its live class, so
  `$Player.Velocity` compiles. `$/root/Main/Player` is absolute and
  `$["My Node"]` takes spaces and punctuation.
- `$Name` does not search the tree: use `ls` to find paths.
- `ls`, `ls $Enemies`, `ls -d 1` (one level), `ls /root/Main`.
- `cd $Main`, `cd /root/Main`, `cd ..`, `cd /`, `pwd`.
- `rm $Enemy` frees a node; `cp $A $Parent` duplicates; `mv $A $Parent` moves;
  `mv $A "NewName"` renames.
- Values print as C# literals; nodes print as `<Area2D> /root/Main/Player`.

## Submissions and timing

- Consecutive C# statements in one call join into one submission: it compiles
  once, runs without frames in between, and runs not at all on a compile error.
- A line without a trailing semicolon ends a submission and prints its value.
- Node commands (`cd`, `ls`, ...) and `:` lines are submissions of their own;
  frames may pass between submissions.
- `#load "file.csx"` runs a whole file as one submission.
- Wait for frames with `await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);`
  and pass `ct` to `Task.Delay`.
- Every session shares the engine thread. A long synchronous loop freezes the
  game for everyone: keep each call short. Loops check `ct`, so a timeout stops
  them; `await tree.ToSignal(...)` cannot be cancelled and keeps running after a
  timeout, with your session busy until it ends.

## Behavior that runs every frame

Godot script classes cannot be added at runtime. Live behavior uses C# events:

```csharp
var step = Callable.From(() => { $Player.Position += Vector2.Right; });
tree.Connect(SceneTree.SignalName.ProcessFrame, step);   // runs every frame
$Player.SetMeta("step", step);
```

- Handlers keep running after your call, and today also after `:reset` and
  `2dog repl close`. Keep a way to remove them, as above: the `Callable` in
  node metadata lets any session run `tree.Disconnect(SceneTree.SignalName.ProcessFrame,
  $Player.GetMeta("step").AsCallable())`. C# events (`tree.ProcessFrame += ...`)
  work too, but only the delegate you added removes them again.
- Look nodes up by path inside handlers, or check `GodotObject.IsInstanceValid`:
  someone may replace the node you captured.
- Read tunable values from metadata (`$Player.GetMeta("speed").AsSingle()`),
  so anyone can change them live without re-sending the handler.
- Exceptions and prints from handlers do not come back to `exec`; they go to
  the host's console and to Godot's log at
  `OS.GetUserDataDir() + "/logs/godot.log"`.

## Sharing the game

- Session variables are private. Share state through nodes and their
  metadata (`SetMeta`, `GetMeta`); a `Callable` in metadata can be called from
  any session.
- Agree on node paths first and only change nodes you own.
- Never close another session, never quit the game, and never free `root` or
  the main scene.
- Simulated input is global: release what you press, in a `finally`.

```csharp
try { Input.ActionPress("ui_left"); for (var i = 0; i < 10; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
finally { Input.ActionRelease("ui_left"); }
```

## Look at the game

```csharp
root.GetViewport().GetTexture().GetImage().SavePng("/tmp/shot.png")
```

Then open the PNG. Screenshots need a host with a window; start it without
`--headless`.

## Shell notes

- Quote code in single quotes, or pass it on stdin with `-` and a quoted
  heredoc (`<<'EOF'`).
- Git Bash on Windows turns `/root/Main` arguments into Windows paths: set
  `MSYS_NO_PATHCONV=1`, or use a heredoc.
