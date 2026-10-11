---
title: 2dog repl
description: "Reference for 2dog repl: attach a terminal to a running REPL host, or run C# and node commands in it from scripts and agents."
---

# `2dog repl`

Talks to a running [REPL host](/hosts/repl): attach another terminal, or send
code as one command that prints its result and exits. That second form suits
scripts and coding agents: each call is an ordinary shell command with output
on stdout, errors on stderr and an exit code.

```bash
2dog repl                                      # attach this terminal
2dog repl exec [code] [-s name] [--timeout seconds]
2dog repl sessions                             # list named sessions
2dog repl close <session>                      # close one and its worlds
```

## Finding the host

Start a host with `dotnet run --project MyGame.repl`. A host running in a
terminal listens automatically; a host started by a script or agent needs
`--repl-listen NAME`, which also keeps it running when its own input ends:

```bash
dotnet run --project MyGame.repl -- --headless --repl-listen mygame
```

Listening hosts publish themselves for `2dog repl` while they run. The host
for the project containing the current directory is used, or the only running
host. When several hosts qualify, the error lists them; choose one with
`--pipe NAME`.

## Running code

```bash
2dog repl exec -s agent 'cd $Main'
2dog repl exec -s agent '$Player.Position'
```

`$` paths start at the selected node and have the node's live type, so
`$Player.Position` compiles where `here.Position` would not. Several arguments form one submission, so `2dog repl exec ls $Player` works
unquoted where your shell allows. Without code, or with `-`, the code comes
from stdin:

```bash
2dog repl exec -s agent --timeout 30 - <<'EOF'
cd $Main
var start = $Player.Position;
$Player.Position += Vector3.Up;
$Player.Position - start
EOF
```

Code is split into submissions the way piped input is: shell commands are one
line, C# blocks continue until complete. Submissions run in order and stop at
the first failure. `:sh`, `:cs`, `:auto`, `:reset` and `:help` work as in the
prompt; `:exit` ends the request without closing the session.

| Option | Effect |
| --- | --- |
| `-s, --session <name>` | Run in a named session. Its variables, imports, mode, selected node and worlds persist between calls until `2dog repl close` or the host stops. Without it, each call gets a fresh session. |
| `--timeout <seconds>` | Cancel the submission after this long. The session and its earlier results remain. |
| `--pipe <name>` | The host to use, by its `--repl-listen` name, when several are running. |

Values print on stdout, one per submission that has one. Compile errors,
exceptions and Godot's errors and warnings go to stderr. `Console.WriteLine`
and `GD.Print` output raised while the submission runs comes back with it,
in order. Output from later frames, timers or background tasks stays on the
host's console.

`--json` prints one report with a result per submission, including its
status, value or error, and what it printed:

```bash
2dog repl exec --json -s agent 'GD.Print("hi"); 6 * 7'
```

```json
"repl": {
  "pipe": "mygame",
  "project": "/home/me/MyGame/",
  "session": "agent",
  "status": "ok",
  "results": [
    { "input": "GD.Print(\"hi\"); 6 * 7", "status": "ok", "value": "42", "log": "hi\n" }
  ]
}
```

| Exit code | Meaning |
| --- | --- |
| `0` | Every submission succeeded |
| `2` | No host found, the host went away, or the request was invalid |
| `3` | A submission failed to compile or threw; later lines did not run |
| `124` | `--timeout` cancelled a submission |
| `130` | Cancelled, with Ctrl+C or because the session was closed |

Interrupting `2dog repl exec`, or killing it, cancels its submission. All
sessions share the engine thread: a synchronous loop without `ct` pauses the
game and every other session until it ends. Loops in submitted code check for
cancellation, so `--timeout`, Ctrl+C or `2dog repl close` stops them.

## Sessions

```bash
2dog repl sessions
```

```text
agent  :auto  /root/Main/Player  default  idle 4s
lab    :cs    /root              -        busy
```

Each row shows the mode, the selected node, open worlds, and whether the
session is running something. While a submission holds the engine thread, the
node and worlds read `(engine busy)` and `?`. `2dog repl close agent` cancels
that session's work, closes its worlds and forgets its variables.

## Attaching a terminal

`2dog repl` starts the host's own client in this terminal, with completion,
highlighting and multiline editing, like
`dotnet run --project MyGame.repl -- --repl-connect NAME`. Each attached
terminal has a session of its own that ends when it disconnects.
