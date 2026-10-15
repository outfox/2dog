---
title: Coding Agents
description: "How coding agents such as Claude Code and Codex work in a running 2dog game through the REPL host, alone or together."
---

# Coding Agents

A [REPL host](/hosts/repl) turns a running game into something a coding agent
can work in: every change is an ordinary shell command,
[`2dog repl exec`](/cli/repl), with its result on stdout and an exit code.
Several agents and people can share one game, each in a named session.

Start a host the agents can reach, then give them the guide:

```bash
dotnet run --project MyGame.repl -- --repl-listen mygame
2dog skill            # Claude Code; add --codex for AGENTS.md
```

[`2dog skill`](/cli/skill) writes the guide below where agents look for project
instructions. A running host prints the same text with `:help agents`.

<!--@include: ../../twodog.repl/Agents/repl-guide.md{3,}-->
