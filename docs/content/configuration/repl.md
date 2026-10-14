---
title: REPL Configuration
description: "Run the REPL with different Godot native variants, headless mode or a regular .NET publish."
---

# REPL Configuration

The generated `MyGame.repl/` host references your game, `2dog.repl` and
`2dog.engine`. Select `2dog.engine.double` for double precision;
the REPL compiles submissions against the bindings loaded by the host.
Choose the Godot native variant with the build configuration:

| Configuration | Native variant |
| --- | --- |
| Debug (default) | `debug` |
| Editor | `editor` |
| Release | `release` |

```bash
dotnet run --project MyGame.repl -c Editor
dotnet run --project MyGame.repl -- --headless
```

Editor runs the game with editor natives; it does not open the Godot editor.
Arguments after `--` go to Godot, except the REPL connection options below. The shared
[engine properties](/configuration#properties) also apply.

## Local connections

Run a listening host in one terminal, then connect from others:

```bash
dotnet run --project MyGame.repl -- --repl-listen mygame
dotnet run --project MyGame.repl -- --repl-connect mygame
```

| Option | Behavior |
| --- | --- |
| `--repl-listen NAME` | Accept concurrent sessions over a local named pipe. |
| `--repl-connect NAME` | Pipe stdin/stdout to a running host without starting another engine. |

The options are mutually exclusive. Connecting accepts no Godot arguments and
times out after five seconds if the host is unavailable. The generated host
supports both options; custom hosts use the `ReplHost.Run(args, createEngine,
references)` overload.

PowerShell example for a second process or agent:

```powershell
'var answer = 40 + 2;', 'answer' | dotnet run --project MyGame.repl -- --repl-connect mygame
```

Each connection owns its variables, node selection, cancellation queue and
named scratch worlds. All sessions share the engine's owner thread and live
scene tree. Pipe input is UTF-8 text with complete submissions or multiline C#
blocks. Interactive clients use the host's full editor, with syntax colors,
Tab completion, multiline input and Ctrl+C cancellation. Completion sees each
connection's variables, selected node and eligible `res://` paths. Redirected
input and output stay plain text; `NO_COLOR` disables colors in a terminal.
Results and errors return to the client; `Console.WriteLine` and Godot logs
remain on the host console. An interactive host redirects its process's
standard output and error, including native writes, and prints them above its
own input line.

Use `:cancel` to interrupt a connection's running and queued work. Client EOF
or `:exit` waits for submitted work and disconnects. Disconnecting abruptly
cancels its work and closes its worlds. A listening host remains available
after its own stdin reaches EOF; local `:exit` or `tree.Quit()` stops it.
The pipe accepts only the current OS user. The REPL is a trusted experimental
tool that executes arbitrary code in the host process.

## Publishing

The REPL compiles C# at runtime. Use a regular folder publish; `PublishAot`,
`PublishTrimmed` and `PublishSingleFile` must be `false`.

```bash
dotnet publish MyGame.repl -c Release
```

See [REPL](/hosts/repl) to get started, and type `:help` in the prompt for
commands and controls.
