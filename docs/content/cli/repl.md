---
title: 2dog repl
description: "Attach to a running REPL host or execute C# and node commands from scripts and agents."
tagline: "Learn how to drive a running game with C# from scripts and coding agents."
---

# `2dog repl`

Connect to a running [REPL host](/hosts/repl) from a terminal, script or coding
agent:

```bash
2dog repl                       # attach this terminal
2dog repl exec [code]            # run code and exit
2dog repl sessions               # list named sessions
2dog repl close <session>        # cancel work and close its worlds
```

## Finding the host

A host that runs in a terminal listens for connections automatically. For
scripts and agents, use `--repl-listen`. Then the host does not stop when stdin
ends:

```bash
dotnet run --project MyGame.repl -- --headless --repl-listen mygame
```

`2dog repl` selects the host for the current project. If there is no such host,
it selects the only running host. If several hosts qualify, choose one with
`--pipe NAME`.

## Running code

Use a named session to keep variables and node selection between calls:

```bash
2dog repl exec -s agent --timeout 30 'cd scene'
2dog repl exec -s agent --timeout 30 '$Player.Position'
```

`$Player` refers to a child of the selected node. Its type is the live C# type
of that child.
Use `ls` to find nodes and `cd /root/Main` to select an absolute path.
`ls -d 1` limits the listing depth.

If you give no code, or give `-`, the command reads the code from stdin:

```bash
2dog repl exec -s agent --timeout 30 - <<'EOF'
var start = $Player.Position;
$Player.Position += Vector3.Up;
$Player.Position - start
EOF
```

Consecutive C# statements compile and run as one submission, with no frames
between them. A line without a trailing semicolon ends the submission and
prints its value. Shell commands and `:` commands run separately. If a submission
fails, the later submissions do not run. `#load "file.csx"` runs a file as one
submission. `:exit` ends the request but does not close the session.

Quote code to protect `$` paths from your shell. If the code starts with `-`,
put it after `--`: `2dog repl exec -- -1 + 2`.

| Option                 | Effect                                                                                                                                                    |
|------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------|
| `-s, --session <name>` | Keep variables, imports, mode, selected node and worlds until you close the session or the host stops. Without this option, each call gets a new session. |
| `--timeout <seconds>`  | Cancel after this duration, including queued time; preserve the session.                                                                                  |
| `--strict`             | Fail if a submission logs a Godot error or warning.                                                                                                       |
| `--pipe <name>`        | Choose a host by its `--repl-listen` name.                                                                                                                |
| `--json`               | Return a structured report with status, values, errors, logs and node listings.                                                                           |

## Output and cancellation

Values go to stdout; compile errors, exceptions and Godot errors and warnings
go to stderr. The command returns the output that
the submission writes with `Console.WriteLine` and `GD.Print`. Output that
comes later stays on the host console. JSON reports contain
per-submission `results` and top-level `errors`; listings include a `tree` of
`name`, `type` and `children`. `hidden` gives the number of omitted descendants.

| Exit code | Meaning                                                |
|-----------|--------------------------------------------------------|
| `0`       | All submissions succeeded                              |
| `1`       | Invalid command line                                   |
| `2`       | Host unavailable, connection broken or request refused |
| `3`       | Submission failed; later submissions did not run       |
| `124`     | Timeout expired or the host did not answer             |
| `130`     | Cancelled by Ctrl+C or session closure                 |

All sessions share the engine thread; synchronous work pauses the game.
Cancellation is cooperative. Pass `ct` to waits such as `Task.Delay`. If you
do not, they continue to run, and the session stays busy after a timeout
returns 124.

## Sessions

`2dog repl sessions` shows each named session's mode, selected node, worlds
and activity. While the engine thread is busy, node and world details are
unavailable. `2dog repl close agent` cancels that session, closes its worlds
and discards its variables. Work that ignores cancellation finishes before
its worlds close.

## Attaching a terminal

`2dog repl` opens the host's interactive client with completion, highlighting
and multiline editing. Each attached terminal has its own session, which
ends when it disconnects. See [local connections](/configuration/repl#local-connections)
for host options.
