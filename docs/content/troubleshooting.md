---
title: Troubleshooting
description: "What the 2dog tool's exit codes, notes and errors mean, how it behaves in CI and pipes, where to look when a restore or build fails, and how to fix a pinned Godot .NET runtime."
tagline: "How to work around common pitfalls and limitations of 2dog, git, and Godot."
---

# Troubleshooting

What's up, ~~dog~~ doc?

![Illus. Diego Rago: a white anthro dog in a hacker hoodie faceplanting in front of their robot dog](img/2dog-trouble.png)

Most problems have a first step in common: run [`2dog doctor`](/cli/doctor) in
the project. It checks the machine and the project, fixes what it safely can,
and explains build failures it recognizes (`2dog doctor --build`, or
`2dog doctor --log <file>` for a log you already have).

## Exit codes

| Code  | Meaning                                         | What to do                                                                                                                                                                                                                     |
|-------|-------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `1`   | Usage error                                     | The `error:` line names the problem and suggests the closest option or verb; `2dog <verb> --help` lists what the verb accepts                                                                                                  |
| `2`   | Tool error                                      | Project state (no `project.godot`, a name with spaces, invalid XML), a failed `dotnet` call, or a plan that stopped half-way. The `error:`/`hint:` lines say which; `--verbose` adds the stack trace and every subprocess line |
| `3`   | Doctor findings remain, or the `--build` failed | Read the report; `--fix` applies the safe fixes, `--ignore <id>` drops one you accept. After `--build`, the build section names the matched failure and the full log                                                           |
| `130` | Cancelled                                       | Ctrl+C during a prompt or a subprocess; nothing after the cancelled step ran                                                                                                                                                   |

## A plan stopped half-way

`2dog new`, `add` and `update` apply their steps in order with no rollback. A
failing step is reported as `error: step N/M failed: ...`, followed by a
`note:` listing the steps that stand and how many did not run. Fix the cause
(a read-only file, the Godot editor holding a csproj open) and run the same
command again: existing files are skipped, `--force` overwrites.

## No terminal, CI, pipes

With stdout not a terminal, or in
[CI](/configuration/environment-variables#cli-output-and-automation), the tool
never prompts.
`new` and `add` without host flags then apply the default host set and print
`note: no terminal to ask on`; pass `--yes` or name the hosts to make that
explicit. `doctor` prints the fix commands instead of asking. Markers fall back
to ASCII; `--json` gives one document on stdout; `--plain` and `NO_COLOR` turn
styling off; `CLICOLOR_FORCE` keeps colour in a pipe.

## `dotnet restore` failed

The tool prints the restore failure, explains recognized errors and returns
exit code `2`. Files already written are kept. Common causes:

- **`NETSDK1147` / wasm-tools**: the browser hosts need the workload -
  `dotnet workload install wasm-tools`.
- **`NETSDK1147` / android**: Android hosts need `dotnet workload install android`.
  Building or publishing Android also requires the Android SDK and JDK 17.
- **`NU1101`/`NU1102`, a `2dog.*` package version not found**: the version the
  project pins was not published yet, or your `nuget.config` hides nuget.org.
  `2dog update` pins the versions the running tool ships.
- **`NU1213`**: a project references the `2dog` tool package; reference
  `2dog.engine` instead.

Run `dotnet restore` again after fixing the cause; `2dog doctor --log` explains
a saved restore log the same way it explains builds.

## "dotnet not found"

Put `dotnet` on `PATH`, or set `DOTNET_HOST_PATH` to the `dotnet` executable.

## Godot cannot load `System.Runtime` (NixOS and pinned .NET runtimes)

After `2dog add`, the stock Godot editor may report:

```text
System.IO.FileNotFoundException: Could not load file or assembly
'System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'.
.NET: Failed to load project assembly.
```

`2dog add` upgrades the game project to `net10.0`, which the 2dog binding
packages require. If the editor loads an older .NET runtime, it cannot load
that assembly even when compilation succeeds. Running `dnx 2dog` successfully
or seeing .NET 10 in `dotnet --list-sdks` does not prove that the editor uses
.NET 10: the editor loads its runtime separately from the build SDK.

### NixOS

Nixpkgs' `godot-mono` wrapper selects its own .NET SDK, which can be older than
the one in your shell. Replace the existing `pkgs.godot-mono` entry in your
NixOS `environment.systemPackages` or Home Manager `home.packages` with:

```nix
(pkgs.godot-mono.overrideAttrs {
  dotnet-sdk = pkgs.dotnet-sdk_10;
})
```

Apply your Nix configuration, fully close Godot, then reopen the project using
the updated package and rebuild. An already running editor keeps its loaded
runtime. Installing .NET 10 alongside the default Godot package alone does not
change the SDK selected by its wrapper. See the
[Nixpkgs Godot wrapper](https://github.com/NixOS/nixpkgs/blob/master/pkgs/development/tools/godot/common.nix)
for the SDK override.

### Other systems and later .NET versions

The same issue can occur with any launcher, package or environment that pins
Godot to an older .NET runtime. Check the launcher's SDK/runtime selection,
including `DOTNET_ROOT` and `PATH`, and configure it to use a runtime compatible
with the game's target framework. Fully restart the editor after changing it.
Keep the game on the target framework required by its 2dog packages.

Godot 4.8's move to .NET 10 reduces this mismatch for `net10.0` projects, but
does not remove the underlying constraint: it can recur when a game targets
a later .NET version than the runtime selected by the editor.

## Mixed Godot bindings (TDG001-TDG005)

2dog swaps stock GodotSharp assemblies for its own at build time. `2dog update`
sets this up in an existing game; `2dog add` and new projects do it already.

| Code     | Cause                                               | Fix                                                                                           |
|----------|-----------------------------------------------------|-----------------------------------------------------------------------------------------------|
| `TDG001` | A dependency targets a different Godot version      | Align it, or `Godot.NET.Sdk`, with the engine's Godot version                                 |
| `TDG002` | `GodotSharpEditor` without `2dog.godotsharp.editor` | Reference `2dog.godotsharp.editor` at the engine version; remove stock packages and HintPaths |
| `TDG003` | A referenced GodotSharp assembly is not 2dog's      | Remove the conflicting reference or HintPath and rebuild against `2dog.godotsharp`            |
| `TDG004` | Core and editor binding packages differ in version  | Use one 2dog version for all of them                                                          |
| `TDG005` | An editor export build found no stock GodotSharp    | Build the game with `Godot.NET.Sdk`                                                           |

## The Godot editor holds a file

`could not rename ...` or `the file is in use`: close the Godot editor and any
IDE with the project open, then re-run. On Windows the editor keeps the game
csproj and its build outputs locked while the project is open.
