---
title: REPL Configuration
description: "Configure REPL native variants, headless runs, local connections and publishing."
tagline: "Learn how to configure the C# REPL for headless runs and publishing."
---

# REPL Configuration

`MyGame.repl/` references your game, `2dog.repl` and `2dog.engine`. For
double precision, use `2dog.engine.double`. Submissions use the bindings that
the host has loaded. The [global settings](/configuration#properties) also apply.

Debug (the default), Editor and Release select the matching
[native variant](/build-configurations).

```bash
dotnet run --project MyGame.repl -c Editor
dotnet run --project MyGame.repl -- --headless
```

The Editor configuration uses the editor natives, but it does not open the
Godot editor. Arguments after
`--` go to Godot, except the connection options below.

## Local connections

Start a host, then attach from another terminal with [`2dog repl`](/cli/repl):

```bash
dotnet run --project MyGame.repl -- --repl-listen mygame
```

```bash
2dog repl --pipe mygame
```

| Host option           | Behavior                                                                |
|-----------------------|-------------------------------------------------------------------------|
| `--repl-listen NAME`  | Accept sessions over a local named pipe; keep running after stdin ends. |
| `--repl-connect NAME` | Connect to that host without starting another engine.                   |

Use only one of these options at a time. `--repl-connect` accepts no Godot
arguments and times out after five seconds. Custom hosts use the
`ReplHost.Run(args, createEngine, references)` overload.

Each connection has its own variables, node selection, queue and scratch
world. All connections share the engine thread and the scene tree. Interactive clients have
completion and multiline editing; redirected I/O uses plain UTF-8 text.
`NO_COLOR` disables terminal colors.

`:cancel` cancels a connection's work. EOF or `:exit` waits for submitted work
and disconnects; an abrupt disconnect cancels work and closes its worlds.
Local `:exit` or `tree.Quit()` stops the listening host. Pipes accept only the
current OS user. A connected client can run any code in the host.

## Publishing

The REPL compiles C# at runtime. `PublishAot`, `PublishTrimmed` and
`PublishSingleFile` must be `false`:

```bash
dotnet publish MyGame.repl -c Release
```
