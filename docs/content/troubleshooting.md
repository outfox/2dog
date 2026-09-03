---
title: Troubleshooting
description: "What the 2dog tool's exit codes, notes and errors mean, how it behaves in CI and pipes, and where to look when a restore or build fails."
---

# Troubleshooting

Most problems have a first step in common: run [`2dog doctor`](/doctor) in
the project. It checks the machine and the project, fixes what it safely can,
and explains build failures it recognizes (`2dog doctor --build`, or
`2dog doctor --log <file>` for a log you already have).

## Exit codes

| Code | Meaning | What to do |
| --- | --- | --- |
| `1` | Usage error | The `error:` line names the problem and suggests the closest option or verb; `2dog <verb> --help` lists what the verb accepts |
| `2` | Tool error | Project state (no `project.godot`, a name with spaces, invalid XML), a failed `dotnet` call, or a plan that stopped half-way. The `error:`/`hint:` lines say which; `--verbose` adds the stack trace and every subprocess line |
| `3` | Doctor findings remain, or the `--build` failed | Read the report; `--fix` applies the safe fixes, `--ignore <id>` drops one you accept. After `--build`, the build section names the matched failure and the full log |
| `130` | Cancelled | Ctrl+C during a prompt or a subprocess; nothing after the cancelled step ran |

## A plan stopped half-way

`2dog new`, `add` and `update` apply their steps in order with no rollback. A
failing step is reported as `error: step N/M failed: ...`, followed by a
`note:` listing the steps that stand and how many did not run. Fix the cause
(a read-only file, the Godot editor holding a csproj open) and run the same
command again: existing files are skipped, `--force` overwrites.

## No terminal, CI, pipes

With stdout not a terminal, or one of `CI`, `GITHUB_ACTIONS`, `TF_BUILD`,
`GITLAB_CI`, `TEAMCITY_VERSION`, `BUILD_NUMBER` set, the tool never prompts.
`new` and `add` without host flags then apply the default host set and print
`note: no terminal to ask on`; pass `--yes` or name the hosts to make that
explicit. `doctor` prints the fix commands instead of asking. Markers fall back
to ASCII; `--json` gives one document on stdout; `--plain` and `NO_COLOR` turn
styling off; `CLICOLOR_FORCE` keeps colour in a pipe.

## `dotnet restore` failed

The tool prints the last lines of the restore and keeps going (the files are
already written). Common causes:

- **`NETSDK1147` / wasm-tools**: the browser hosts need the workload -
  `dotnet workload install wasm-tools`.
- **`NU1101`/`NU1102`, a `2dog.*` package version not found**: the version the
  project pins was not published yet, or your `nuget.config` hides nuget.org.
  `2dog update` pins the versions the running tool ships.
- **`NU1213`**: a project references the `2dog` tool package; reference
  `2dog.engine` instead.

Run `dotnet restore` again after fixing the cause; `2dog doctor --log` explains
a saved restore log the same way it explains builds.

## "dotnet not found"

The tool runs `dotnet sln`, `dotnet restore` and (for doctor) `dotnet build`
through the muxer that launched it (`DOTNET_HOST_PATH`), then `DOTNET_ROOT`,
then `PATH`. Set `DOTNET_HOST_PATH` to the `dotnet` executable when none of
those apply.

## Testing locally packed packages

When you develop 2dog itself, same-version repacks are masked by the NuGet
cache and by nuget.org. Pack with a prerelease suffix CI can never publish and
point the consumer at the local feed:

```bash
dotnet pack twodog.engine -c Release -p:TwoDogVersion=:2dog-version:-local.1
# consumer nuget.config: add the repo's packages/ folder as a source, reference -local.1
```

## The Godot editor holds a file

`could not rename ...` or `the file is in use`: close the Godot editor and any
IDE with the project open, then re-run. On Windows the editor keeps the game
csproj and its build outputs locked while the project is open.
