---
title: Doctor and Update
description: "2dog doctor checks a project and the machine, applies safe fixes, and explains build failures; 2dog update brings the 2dog packages to the tool's versions."
---

# Doctor and Update

`2dog doctor` looks at a 2dog project the way the build will, before the build
does: the machine, the layout, every csproj, the solution, the package
versions, the export presets, and `project.godot`. It prints one line per area,
expands what is wrong, and offers the fixes it can apply. `2dog update` is its
companion for the one thing doctor never does on its own: changing package
versions.

```bash
dnx 2dog doctor            # check, then ask which fixes to apply
dnx 2dog doctor --fix      # apply the safe fixes unattended, then check again
dnx 2dog update            # bring the 2dog packages to this tool's versions
```

## What a run looks like

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

The default run is static and fast: it reads files, asks `dotnet` for the
installed SDKs and workloads, and (unless `--offline`) asks nuget.org whether a
newer tool exists. It never builds unless you pass `--build`.

On a terminal, doctor ends with a checklist of the fixes it found: safe ones
start checked, announced ones unchecked. In a pipe or with `--yes` it prints
the commands instead. `-v` lists every check, passed ones included, and
`--list-checks` prints the catalogue.

## Fix classes

Every fix respects the tool's rule: it creates files or edits `*.csproj`,
`project.godot`, `export_presets.cfg`, the solution and `Directory.Build.props`
in place, and never moves, renames or deletes anything without saying so.

| Class | Applied by | Examples |
| --- | --- | --- |
| **safe** | `--fix`, or pre-checked in the interactive list | create a missing `.gdignore`, `global.json` or `Directory.Build.targets`; append a missing export preset; add the properties a host needs; add projects to the solution; exclude browser and WinUI hosts from plain solution builds; set `[xr] shaders/enabled.web` |
| **announced** | `--fix-all`, or ticked by you | migrate `.sln` to `.slnx` (deletes the `.sln`); upgrade the game csproj to `net10.0`; refresh the tool-owned `TwoDogWebBoot.cs` |
| **by hand** | you, following the `run:` line | install an SDK or workload; remove `PublishAot`; fix a broken `ProjectReference`; and everything about versions, which is [`2dog update`](#updating-a-project)'s job |

After applying anything, doctor checks again and prints the result under
`re-check`.

## Exit codes and CI

Doctor exits `0` when nothing but informational items remain, `3` while errors
remain (or warnings, with `--strict`), and `2` when it could not run at all
(no `project.godot`, unreadable files). `--json` puts the whole report in one
document on stdout:

```bash
2dog doctor --json --strict | jq '.doctor.summary'
```

Drop a finding you have decided to live with by id: `--ignore ver.tool-latest`.
The ids are stable and listed by `--list-checks`.

## Explaining build failures

```bash
2dog doctor --build                     # builds the solution
2dog doctor --build MyGame.web -c Release
2dog doctor --log build.log             # explains a log you already have
dotnet build 2>&1 | 2dog doctor --log - # or one piped in
```

`--build` runs `dotnet build` with plain-text output, writes the full log to
a file under your temp directory (the path is printed), and matches the output
against the failures 2dog knows: its own MSBuild errors (a `TwoDogVariant`
typo, a missing export preset, the `Godot.NET.Sdk`/`2dog.engine` line
mismatch, missing natives or import capability, `PublishAot`), the SDK and
NuGet errors 2dog projects run into (`NETSDK1147` for wasm-tools, `NU1213` for
referencing the tool package, `NU1101` for an unpublished version), and the
runtime loader messages. Each match gets a title, the offending line, and the
command that fixes it; other errors are listed after; a log with no errors at
all shows its last lines.

## Updating a project

```bash
git status            # update wants a clean tree, so the diff is easy to review
dnx 2dog update       # to the newest tool's versions
dnx 2dog@:2dog-version: update   # to a specific tool's versions
```

`2dog update` always targets the versions of the tool that runs it; there is
no `--to`. It plans, shows the plan (`--dry-run` stops there), then:

1. moves literal package versions still in host csprojs into the shared
   `Directory.Build.props` block (projects created by older tools);
2. sets `TwoDogVersion`, `TwoDogNativesVersion` and `TwoDogGodotVersion` in
   that block, and raises the companion versions (Avalonia, Windows App SDK,
   ASP.NET Core) when the tool's are newer - a companion the project already
   has at a newer version keeps it;
3. sets the game project's `Godot.NET.Sdk` version to match;
4. refreshes the tool-owned `TwoDogWebBoot.cs` when it drifted;
5. runs `dotnet restore` (skip with `--no-restore`; a failure is explained like
   a build failure and the update exits with code 2, the files already
   rewritten).

It never downgrades: a project ahead of the tool stops with the advice to run
the newest tool. Crossing a Godot line (4.7 to 4.8) is applied but announced:
install the matching Godot .NET editor, open the project once so it rewrites
`config/features`, then run `2dog doctor --build`.

The check refuses a dirty git working tree unless you pass `--allow-dirty`;
the tool never runs a git write itself.

## Checks

The ids below are what `--ignore` and the JSON report use. Run
`2dog doctor --list-checks` for the list your tool version ships.

| Area | Ids |
| --- | --- |
| environment | `env.dotnet-sdk`, `env.global-json`, `env.wasm-tools`, `env.host-platform`, `env.godot-editor`, `env.overrides`, `env.packages-restored` |
| layout | `layout.load-problems`, `layout.game-csproj`, `layout.assembly-name`, `layout.multiple-root-csproj`, `layout.spaced-name`, `layout.legacy-root-webboot`, `layout.root-build-targets`, `layout.root-build-props`, `layout.root-global-json` |
| game csproj | `game.sdk`, `game.sdk-mismatch`, `game.target-framework`, `game.properties`, `game.default-item-excludes`, `game.webboot-include`, `game.webboot-duplicate` |
| hosts | `host.gdignore`, `host.project-reference`, `host.godot-project-dir`, `host.variant`, `host.buildtype-deprecated`, `host.publish-aot`, `host.duplicate-analyzers`, `host.app-manifest`, `host.web-props-shim`, `host.web-global-json`, `host.webboot-drift`, `host.trimmer-root`, `host.blazor-client`, `host.windows-only` |
| solution | `sln.exists`, `sln.multiple`, `sln.legacy-format`, `sln.contains-game`, `sln.contains-hosts`, `sln.build-exclusions` |
| versions | `ver.managed-elsewhere`, `ver.props-invalid`, `ver.literal-versions`, `ver.twodog-consistent`, `ver.twodog-outdated`, `ver.twodog-newer`, `ver.natives`, `ver.godot-line-consistent`, `ver.godotsharp-editor`, `ver.companions`, `ver.tool-latest` |
| presets | `preset.file`, `preset.web`, `preset.desktop` |
| godot settings | `godot.features-line`, `godot.xr-shaders`, `godot.import-stamp` |

Projects that express versions through their own MSBuild properties (a
monorepo's `Directory.Build.props`, say) are noted as `ver.managed-elsewhere`
and otherwise left alone; files inherited from a parent directory
(`Directory.Build.targets`, `global.json`) are reported as informational, not
as missing.
