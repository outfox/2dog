---
title: 2dog skill
description: "Reference for 2dog skill: write the REPL working guide where Claude Code, Codex and other coding agents read project instructions."
---

# `2dog skill`

Writes the guide for working in a live game through
[`2dog repl exec`](/cli/repl) where coding agents look for it: the globals,
`$` paths, timing on the shared engine thread, handlers that run every frame,
and how several agents share one game.

```bash
2dog skill                    # .claude/skills/2dog-repl/SKILL.md for Claude Code
2dog skill --codex            # a marked section of AGENTS.md
2dog skill --claude --codex   # both
2dog skill path/to/MyGame --dry-run
```

| Option | Effect |
| --- | --- |
| `--claude` | Write `.claude/skills/2dog-repl/SKILL.md`, which Claude Code loads when a task needs it. This is the default. |
| `--codex` | Write the guide between `<!-- 2dog:repl-guide -->` markers in `AGENTS.md`, which Codex and other agents read. Later runs replace only that section. |
| `--dry-run` | Show what would change without writing. |

Run it again after updating the tool to refresh the guide; unchanged files are
left alone. `2dog new --repl` and `2dog add --repl` write the Claude skill along
with the host. The same text prints in a running REPL with `:help agents`.
