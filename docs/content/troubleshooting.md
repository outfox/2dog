---
title: Troubleshooting
description: "Learn what 2dog's exit codes and errors mean, how the tool behaves in CI and pipes, and how to fix restore, build and runtime problems."
tagline: "Learn how to fix common problems with 2dog, .NET and Godot."
---

# Troubleshooting

What's up, ~~dog~~ doc?

![Illus. Diego Rago: a white anthro dog in a hacker hoodie faceplanting in front of their robot dog](img/2dog-trouble.png)

For most problems, the first step is the same: run [`2dog doctor`](/cli/doctor)
in the project. It checks the machine and the project, and it applies the fixes
that are safe. It also explains build failures that it recognizes. To build
the project and explain the failures, run `2dog doctor --build`. For a log that
you already have, run `2dog doctor --log <file>`.

## Exit codes

| Code  | Meaning                                         | What to do                                                                                                                                                                                                                                                    |
|-------|-------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `1`   | Usage error                                     | The `error:` line names the problem and suggests the closest option or verb; `2dog <verb> --help` lists what the verb accepts                                                                                                                                 |
| `2`   | Tool error                                      | Read the `error:` and `hint:` lines. They name the cause: the project state (no `project.godot`, a name with spaces, invalid XML), a failed `dotnet` call, or a plan that stopped half-way. To see the stack trace and every subprocess line, add `--verbose` |
| `3`   | Doctor findings remain, or the `--build` failed | Read the report. To apply the safe fixes, add `--fix`. To ignore a finding that you accept, add `--ignore <id>`. After `--build`, the build section names the matched failure and the full log                                                                |
| `130` | Cancelled                                       | Ctrl+C during a prompt or a subprocess; nothing after the cancelled step ran                                                                                                                                                                                  |

## A plan stopped half-way

`2dog new`, `add` and `update` apply their steps in order with no rollback. If
a step fails, the tool reports `error: step N/M failed: ...`. Then a `note:`
lists the completed steps and the number of steps that did not run. Fix the cause,
for example a read-only file or a Godot editor that holds a csproj open. Then
run the same command again. The tool skips existing files; to overwrite them,
add `--force`.

## No terminal, CI, pipes

If stdout is not a terminal, or if the tool runs in
[CI](/configuration/environment-variables#cli-output-and-automation), the tool
never prompts.
If you run `new` or `add` without host flags, they apply the default host set
and print `note: no terminal to ask on`. To make that choice explicit, pass
`--yes` or name the hosts. `doctor` prints the fix commands instead of asking.

Output for scripts and pipes:

- Markers use ASCII characters.
- `--json` writes one document to stdout.
- `--plain` and `NO_COLOR` turn styling off.
- `CLICOLOR_FORCE` keeps color in a pipe.

## `dotnet restore` failed

The tool prints the restore failure, explains recognized errors and returns
exit code `2`. Files already written are kept. Common causes:

- **`NETSDK1147` / wasm-tools**: the browser hosts need the `wasm-tools`
  workload. Run `dotnet workload install wasm-tools`.
- **`NETSDK1147` / android**: Android hosts need the `android` workload.
  Run `dotnet workload install android`. To build or publish for Android, you
  also need the Android SDK and JDK 17.
- **`NU1101`/`NU1102`, a `2dog.*` package version not found**: the version that
  the project pins is not published yet, or your `nuget.config` hides
  nuget.org. To pin the versions that the running tool ships, run `2dog update`.
- **`NU1213`**: a project references the `2dog` tool package; reference
  `2dog.engine` instead.

After you fix the cause, run `dotnet restore` again. `2dog doctor --log` also
explains a saved restore log, in the same way as a build log.

## "dotnet not found"

The tool cannot find the `dotnet` executable. Add the folder that contains
`dotnet` to `PATH`, or set `DOTNET_HOST_PATH` to the full path of the `dotnet`
executable.

## Godot cannot load `System.Runtime` (NixOS and pinned .NET runtimes)

After `2dog add`, the stock Godot editor may report:

```text
System.IO.FileNotFoundException: Could not load file or assembly
'System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'.
.NET: Failed to load project assembly.
```

`2dog add` upgrades the game project to `net10.0`, which the 2dog binding
packages require. If the editor loads an older .NET runtime, it cannot load
that assembly even when compilation succeeds. Even if `dnx 2dog` runs
and `dotnet --list-sdks` shows .NET 10, the editor can use an older runtime.
The editor loads its runtime separately from the build SDK.

### NixOS

Nixpkgs' `godot-mono` wrapper selects its own .NET SDK, which can be older than
the one in your shell. Replace the existing `pkgs.godot-mono` entry in your
NixOS `environment.systemPackages` or Home Manager `home.packages` with:

```nix
(pkgs.godot-mono.overrideAttrs {
  dotnet-sdk = pkgs.dotnet-sdk_10;
})
```

1. Apply your Nix configuration.
2. Close Godot fully. An editor that is already running keeps its loaded
   runtime.
3. Open the project with the updated package.
4. Build the project again.

If you only install .NET 10 next to the default Godot package, the wrapper
still selects its own SDK. See the
[Nixpkgs Godot wrapper](https://github.com/NixOS/nixpkgs/blob/master/pkgs/development/tools/godot/common.nix)
for the SDK override.

### Other systems and later .NET versions

The same issue can occur with any launcher, package or environment that pins
Godot to an older .NET runtime. Check which SDK and runtime the launcher
selects, including `DOTNET_ROOT` and `PATH`. Configure the launcher to use a
runtime that is compatible with the game's target framework. After you change
the launcher, fully restart the editor.
Keep the game on the target framework required by its 2dog packages.

Godot 4.8 moves to .NET 10, so this mismatch is less common for `net10.0`
projects. But the problem can occur again when a game targets a later .NET
version than the runtime that the editor selects.

## Mixed Godot bindings (TDG001-TDG005)

At build time, 2dog replaces the stock GodotSharp assemblies with its own.
`2dog update` sets this up in an existing game; `2dog add` and new projects do
it already.

| Code     | Cause                                               | Fix                                                                                           |
|----------|-----------------------------------------------------|-----------------------------------------------------------------------------------------------|
| `TDG001` | A dependency targets a different Godot version      | Change the dependency, or `Godot.NET.Sdk`, to the engine's Godot version                      |
| `TDG002` | `GodotSharpEditor` without `2dog.godotsharp.editor` | Reference `2dog.godotsharp.editor` at the engine version; remove stock packages and HintPaths |
| `TDG003` | A referenced GodotSharp assembly is not 2dog's      | Remove the conflicting reference or HintPath and rebuild against `2dog.godotsharp`            |
| `TDG004` | Core and editor binding packages differ in version  | Use one 2dog version for all of them                                                          |
| `TDG005` | An editor export build found no stock GodotSharp    | Build the game with `Godot.NET.Sdk`                                                           |

## The Godot editor holds a file

If the tool reports `could not rename ...` or `the file is in use`, another
program has locked a project file. On Windows, the Godot editor locks the game
csproj and its build outputs while the project is open. Close the Godot editor
and any IDE that has the project open. Then run the command again.
