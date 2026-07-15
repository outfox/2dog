# Converting a Godot Project

`2dog convert` turns an existing Godot project into a 2dog project  –  **in
place**. Your project directory becomes the solution root, and the 2dog host
projects (desktop, web, tests) are scaffolded as nested subfolders that the
Godot editor ignores. Nothing is ever moved, renamed, or deleted.

The command ships in the `2dog` package  –  the dotnet tool and the `dotnet new`
template in one, separate from the `2dog.engine` library  –  see
[the FAQ](/faq#why-is-the-library-a-separate-package-2dog-engine-instead-of-part-of-2dog)
for why. It embeds the same template content as `dotnet new 2dog`, so a
converted project and a fresh one have identical layouts.

## Usage

One-shot, no install (.NET 10+ SDK):

```bash
dnx 2dog convert path/to/your/godot/project
```

Or install the `2dog` command globally:

```bash
dotnet tool install -g 2dog
2dog convert path/to/your/godot/project
```

The path defaults to the current directory and must contain a
`project.godot`. Use `--dry-run` first to see the planned actions without
changing anything.

::: tip From stock Godot to the browser
Convert-then-publish is the fastest route to a browser (WebAssembly) release
of an existing Godot project:

```bash
dnx 2dog convert path/to/MyGame
cd path/to/MyGame
dotnet publish MyGame.web -c Release   # static site in MyGame.web/AppBundle/
```

One-time prerequisite: `dotnet workload install wasm-tools`. See
[Web / Browser](/web) for the full story.
:::

## Options

| Option | Effect |
| --- | --- |
| `--name <BaseName>` | Override the derived project base name |
| `--no-web` | Skip the browser (WebAssembly) host project |
| `--no-tests` | Skip the xUnit test project |
| `--dry-run` | Print planned actions without changing anything |
| `--force` | Overwrite scaffolded files that already exist (never deletes/moves) |
| `--no-restore` | Skip the final `dotnet restore` |
| `--verbose` | Extra output |

## Resulting layout

```
MyGame/                      <- your existing Godot project (unchanged)
  project.godot
  MyGame.csproj              <- created or minimally patched
  MyGame.sln                 <- created, or your existing sln is reused
  TwoDogWebBoot.cs           <- added (web bootstrap, guarded by LIBGODOT_ENABLED)
  export_presets.cfg         <- created, or a 'Web' export preset is appended
  global.json                <- added (pins a wasm-capable SDK; skipped if you have one)
  MyGame.2dog/   (.gdignore) <- desktop host (your Main entry point)
  MyGame.web/    (.gdignore) <- browser (WebAssembly) host
  MyGame.tests/  (.gdignore) <- xUnit test project
```

Afterwards:

```bash
dotnet run --project MyGame.2dog           # desktop host
dotnet test MyGame.tests                   # xUnit tests (headless Godot)
dotnet publish MyGame.web -c Release       # browser bundle (needs wasm-tools)
```

The root `global.json` pins a .NET 10 SDK with the wasm-tools workload, which
is what lets the web host publish from the project root (`global.json` applies
at or below its own directory). If your project already has a `global.json`,
convert leaves it untouched  –  make sure it pins a wasm-capable SDK, or publish
from inside `MyGame.web/`, whose own `global.json` wins there.

## What it does

- **Creates or minimally patches the Godot csproj**: `EnableDynamicLoading`,
  `AllowUnsafeBlocks`, the `LIBGODOT_ENABLED` define, and `DefaultItemExcludes`
  for the nested host folders. An existing csproj gets a single marked
  `<PropertyGroup>` appended containing only the properties it was missing  – 
  your file is otherwise left as-is.
- **Adds `TwoDogWebBoot.cs`** to the Godot project (even with `--no-web`: it is
  `#if LIBGODOT_ENABLED`-guarded and inert until a web host uses it, so adding
  web later just works).
- **Adds a root `global.json`** (unless `--no-web`) pinning a .NET 10 SDK with
  the wasm-tools workload, so the web host publishes from the project root. An
  existing `global.json` is never touched, not even with `--force`  –  it is
  your SDK policy, and a warning explains what it needs to pin.
- **Ensures a `Web` export preset exists**: the web host's publish exports
  your project as a `.pck` via that preset, and the engine refuses to export
  without an `export_presets.cfg`. A missing file is created from the
  template; an existing file gets the `Web` preset appended under the next
  free preset index  –  your presets are never touched.
- **Scaffolds the nested host projects**, each with a `.gdignore` file so the
  Godot editor, importer, and exporter skip them.
- **Reuses your solution**: an existing `.sln`/`.slnx` at the project root is
  used as-is (projects are added to it); with none, `<Name>.sln` is created.
  The web host gets ActiveCfg-only entries (no `.Build.0`), so "Build
  Solution" works without the wasm-tools workload  –  the web host is built
  explicitly via `dotnet publish`.
- **Runs `dotnet restore`** at the end (skip with `--no-restore`). A
  wasm-related restore failure is only a warning, with a pointer to
  `dotnet workload install wasm-tools`.

Re-running `2dog convert` on an already-converted project is a no-op
("Nothing to do"). Files that already exist are skipped and reported; pass
`--force` to overwrite the scaffolded files with fresh template content.

## What it never does

- Move, rename, or delete any file.
- Touch version control  –  no `.gitignore` edits, no staging, no commits.
  Review the diff yourself and commit when happy.
- Add a second solution: if multiple solutions at the project root contain
  the Godot project, the tool errors instead of guessing  –  the same rule the
  Godot editor enforces.

## Base name derivation

The base name names the Godot csproj, the solution, and the host folders
(`<Name>.2dog` etc.). It is derived in priority order:

1. `[dotnet] project/assembly_name` in `project.godot` (authoritative: the
   Godot editor resolves `res://<assembly_name>.csproj` from it),
2. the name of the sole `.csproj` at the project root,
3. `application/config/name`, sanitized to a valid file/assembly stem,
4. the project folder name.

`--name` overrides the derivation, but must not conflict with an existing
`assembly_name` in `project.godot`  –  the csproj has to stay named after the
assembly, or the Godot editor won't find it.

## GDScript-only projects

Projects without any csproj work too: `2dog convert` creates a
`Godot.NET.Sdk` csproj and appends a `[dotnet]` section with
`project/assembly_name` to `project.godot`, so the Godot editor picks the
project up as a C# project. Your GDScript scenes and scripts keep running
unchanged  –  the conversion just adds the .NET entry points (and the option to
mix in C#) around them.

## Requirements

- **.NET 10 SDK** (also what `dnx` needs for one-shot execution).
- The **wasm-tools workload** only for publishing the web host
  (`dotnet workload install wasm-tools`)  –  everything else builds without it.
