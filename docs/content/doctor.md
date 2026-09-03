---
title: Doctor and Update
description: "2dog doctor checks a project and the machine, applies safe fixes, and explains build failures; 2dog update brings the 2dog packages to the tool's versions."
---

# Doctor and Update

`2dog doctor` checks a 2dog project without building it: the machine (.NET
SDK, workloads, restored packages), the layout, the game and host csprojs, the
solution, the package versions, the export presets and `project.godot`. It
prints one line per area, expands the findings, and offers the fixes it can
apply. `2dog update` changes package versions, which doctor never does itself.

```bash
dnx 2dog doctor            # check, then ask which fixes to apply
dnx 2dog doctor --fix      # apply the safe fixes unattended, then re-check
dnx 2dog update            # bring the 2dog packages to this tool's versions
```

```text
project  MyGame (P:\games\MyGame)
hosts    MyGame.2dog (desktop), MyGame.web (browser), MyGame.tests (tests)

✓ environment    .NET SDK 10.0.303, wasm-tools, win-x64, packages restored
✓ layout         files parse, MyGame.csproj, assembly_name MyGame, name, ...
✓ game csproj    Godot.NET.Sdk/:godot-version:, matches 2dog.engine, net10.0, ...
✗ hosts
  ✗ MyGame.web/.gdignore missing
      without it the Godot editor imports the host's sources and outputs
      fix: create MyGame.web/.gdignore (safe)
✓ solution       MyGame.slnx, 4 projects, build exclusions
! versions
  ! 2dog packages 4.7.2.61 -> :2dog-version: available
      run: 2dog update
✓ presets        Web, Windows Desktop, Linux, macOS
✓ godot settings Godot 4.7

2 issues (1 error, 1 warning): 1 safe fix, 0 announced, 1 by hand.
apply the safe fixes:      2dog doctor --fix
```

## Fixes

Fixes come in three classes. **Safe** fixes create missing files or add
missing properties, presets and solution entries. **Announced** fixes change
more than that (migrate `.sln` to `.slnx`, upgrade the game csproj to
`net10.0`, refresh `TwoDogWebBoot.cs`). **By hand** items print a `run:` line
for you: installing SDKs, removing `PublishAot`, and every version change,
which belongs to [`2dog update`](#updating-a-project).

In a terminal, doctor ends with a checklist: safe fixes pre-checked, announced
ones unchecked. In a pipe or with `--yes` it prints the commands instead.
After applying fixes it checks again.

## Options

- `--fix` applies the safe fixes; `--fix-all` applies the announced ones too.
- `--build [target]` runs `dotnet build` (of the solution, or a host folder or
  project) and explains the failures 2dog recognizes; `-c` picks the
  configuration.
- `--log <file>` explains an existing build, restore or runtime log; `-` reads
  stdin.
- `--ignore <id>` drops a finding by its check id; `--list-checks` prints the
  ids.
- `--strict` makes warnings fail the run.
- `--offline` skips the nuget.org check for a newer tool.
- `--json` writes the whole report to stdout; `-v` lists passed checks too.

Exit codes: `0` when only informational items remain, `3` while errors remain
(or warnings under `--strict`), `2` when doctor could not run at all.

```bash
2dog doctor --json --strict | jq '.doctor.summary'
dotnet build 2>&1 | 2dog doctor --log -
```

## Updating a project

```bash
dnx 2dog update                  # to this tool's versions
dnx 2dog@:2dog-version: update   # to a specific tool's versions
```

`2dog update` sets the `TwoDog*` versions in `Directory.Build.props` (moving
literal versions out of older host csprojs first), matches the game project's
`Godot.NET.Sdk`, raises the companion packages (Avalonia, Windows App SDK,
ASP.NET Core) when the tool's are newer, refreshes `TwoDogWebBoot.cs`, and runs
`dotnet restore`. It always targets the running tool's versions and never
downgrades. Crossing a Godot line (4.7 to 4.8) is announced: install the
matching editor, open the project once, then run `2dog doctor --build`.

- `--dry-run` shows the plan and stops.
- `--no-restore` skips the restore.
- `--allow-dirty` proceeds on a dirty git tree, which update otherwise refuses.
