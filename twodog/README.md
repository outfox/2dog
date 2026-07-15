# 2dog

Command-line tool and project templates for [2dog](https://2dog.dev)  –  run
Godot as a library from your own .NET entry point. The engine library itself
is the [`2dog.engine`](https://www.nuget.org/packages/2dog.engine) package.

This one package is both a dotnet tool and a `dotnet new` template package.

## New project

```bash
dotnet new install 2dog
dotnet new 2dog -n MyGame
cd MyGame
```

## Convert an existing Godot project

One-shot (no install, .NET 10+):

```bash
dnx 2dog convert path/to/your/godot/project
```

Or install the `2dog` command globally:

```bash
dotnet tool install -g 2dog
2dog convert path/to/your/godot/project
```

## `2dog convert`

Converts an existing Godot project to 2dog **in place**  –  no files are ever
moved, renamed or deleted. The Godot project directory becomes the solution
root, and host projects are scaffolded as nested subfolders that the Godot
editor ignores (each carries a `.gdignore`):

```
MyGame/                      <- your existing Godot project (unchanged)
  project.godot
  MyGame.csproj              <- created or minimally patched
  MyGame.sln                 <- created, or your existing sln is reused
  TwoDogWebBoot.cs           <- added (web bootstrap, guarded by LIBGODOT_ENABLED)
  MyGame.2dog/   (.gdignore) <- desktop host (your Main entry point)
  MyGame.web/    (.gdignore) <- browser (WebAssembly) host
  MyGame.tests/  (.gdignore) <- xUnit test project
```

`dotnet new 2dog` produces the same layout from scratch.

Options:

| Option | Effect |
| --- | --- |
| `--name <BaseName>` | Override the derived project base name |
| `--no-web` | Skip the browser (wasm) host |
| `--no-tests` | Skip the xUnit test project |
| `--dry-run` | Print planned actions without changing anything |
| `--force` | Overwrite files that already exist (never deletes/moves) |
| `--no-restore` | Skip the final `dotnet restore` |
| `--verbose` | Extra output |

## Using the library directly

This package cannot be referenced from a project (it is a tool package);
reference the engine instead:

```bash
dotnet add package 2dog.engine
```
