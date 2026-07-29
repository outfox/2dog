---
title: Project Layout
description: "The recommended 2dog layout: the directory containing project.godot is the .NET solution root, with small nested host projects for desktop, browser, and tests that Godot ignores."
---

# Project Layout

2dog recommends using the directory containing `project.godot` as the .NET
solution root, with a small nested project for each host. Godot keeps its
familiar layout while desktop, tests, and the browser get independent entry points.

## The Layout at a Glance

```text
MyGame/                         Godot project and solution root
├── project.godot               Godot settings and main-scene selection
├── MyGame.csproj               Godot C# game assembly
├── MyGame.slnx                 One solution for the complete project
├── scenes/                     Scenes and resources
├── scripts/                    C# game scripts
├── MyGame.2dog/
│   ├── .gdignore
│   ├── MyGame.2dog.csproj
│   └── Program.cs              Desktop entry point
├── MyGame.tests/
│   ├── .gdignore
│   ├── MyGame.tests.csproj
│   └── BasicTests.cs           Headless xUnit tests
└── MyGame.web/
    ├── .gdignore
    ├── MyGame.web.csproj
    └── wwwroot/                Browser shell and static files
```

Organize scenes and scripts however you prefer. Keep game content beside
`project.godot` and executable hosts in ignored subdirectories.

## One Game Assembly

`MyGame.csproj` remains the Godot C# project. It owns:

- C# game scripts, generated source, and game-assembly configuration
- The assembly Godot loads for your game
- The `Godot.NET.Sdk` configuration
- Browser bootstrap code shared with the web host

Scenes, resources, and assets remain beside `project.godot`. Hosts reference
the game assembly and content; they do not duplicate either.

## Three Ways to Run It

### Generic Host

`MyGame.2dog` starts the embedded engine, points it at the parent Godot project,
and drives the main loop.

```bash
dotnet run --project MyGame.2dog
```

### Test Host

`MyGame.tests` starts Godot through an xUnit fixture, normally headless, and
loads the real game assembly and resources.

```bash
dotnet test MyGame.tests
```

### Browser Host

`MyGame.web` is a .NET WebAssembly host. Publishing it builds the managed
application, imports and exports the Godot content, and assembles a static
site.

```bash
dotnet publish MyGame.web
```

## Why the Hosts Are Nested

Nesting lets hosts find the Godot project. `.gdignore` prevents the editor from
mistaking host source files for game scripts.

Every host points its `GodotProjectDir` property at the parent directory:

```xml
<GodotProjectDir>..</GodotProjectDir>
```

The path is embedded as assembly metadata, used at runtime, and enables
automatic resource import during builds. See [Resource Import](/import-tool).

The game project excludes host folders from its default .NET compile globs, so
the layers do not consume each other's source files.

## What the Godot Editor Sees

Open `MyGame/` in Godot as usual:

```bash
godot-mono --editor MyGame
```

The hosts' `.gdignore` files hide them from the editor, importer, and exporter.

## How You Get This Layout

For an existing project, conversion adds the hosts around your current files:

```bash
cd path/to/MyGame && dnx 2dog add
```

For a fresh project, the same tool creates the game and hosts together:

```bash
dnx 2dog new MyGame
```

See [Adding 2dog](/add) for patching behavior or [Project Templates](/templates)
for every generated file and option.

## When to Use Another Layout

An existing .NET application can host a Godot project elsewhere. Prefer the
standard layout unless an established repository or application boundary gives
you a concrete reason not to.

For one minimal alternative, see the
[Parallel xUnit Collections Demo](https://github.com/outfox/2dog/tree/main/demos/xunit/parallel_collections),
which uses a small Godot project stub beside MSBuild projects with native dependencies.
