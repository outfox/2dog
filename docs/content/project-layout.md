---
title: Project Layout
description: "The recommended 2dog layout: the directory that contains project.godot is the .NET solution root, and small host projects for desktop, browser, and tests sit in folders that Godot ignores."
tagline: "Learn how to keep your game, hosts and tests in one tidy solution."
---

# Project Layout

2dog recommends this layout: the directory that contains `project.godot` is
the .NET solution root, and each host is a small nested project. 

![Illus. Moritz Voss: a white anthro dog in a hacker hoodie and their blue robot dog checking a trail map](img/2dog-trail-map.png)
*"So this is where everything is. Easy!"*

This way you keep your familiar Godot-style project layout while 2dog's hosts sit obediently in ~~their baskets~~ your solution.


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
├── MyGame.xunit/
│   ├── .gdignore
│   ├── MyGame.xunit.csproj
│   └── BasicTests.cs           Headless xUnit tests
└── MyGame.web/
    ├── .gdignore
    ├── MyGame.web.csproj
    └── wwwroot/                Browser shell and static files
```

Organize scenes and scripts however you prefer. Keep game content beside
`project.godot`. Keep executable hosts in subdirectories that Godot ignores.

## One Game Assembly

`MyGame.csproj` remains the Godot C# project. It owns:

- C# game scripts, generated source, and game-assembly configuration
- The assembly Godot loads for your game
- The `Godot.NET.Sdk` configuration
- Browser bootstrap code shared with the web host

Scenes, resources, and assets remain beside `project.godot`. Hosts reference
the game assembly and content; they do not duplicate either.

## Three Ways to Run It

### 2dog (generic .NET)

`MyGame.2dog` starts the embedded engine, points it at the parent Godot project,
and drives the main loop.

```bash
dotnet run --project MyGame.2dog
```

### Test Host

`MyGame.xunit` starts Godot through an xUnit fixture, normally headless, and
loads the real game assembly and resources.

```bash
dotnet test MyGame.xunit
```

The root `global.json` selects Microsoft.Testing.Platform, so plain
`dotnet test` from the root runs every xUnit, NUnit and GdUnit host.

### Web Host

`MyGame.web` is a .NET WebAssembly host. When you publish it, 2dog builds the managed
application, imports and exports the Godot content, and assembles a static
site.

```bash
dotnet publish MyGame.web
```

## Why the Hosts Are Nested

Each host sits one level below the Godot project, so it can find the project.
`.gdignore` tells the editor to ignore host source files, so it does not treat
them as game scripts.

Every host points its `GodotProjectDir` property at the parent directory:

```xml
<GodotProjectDir>..</GodotProjectDir>
```

The build embeds the path as assembly metadata. The host reads it at runtime,
and the build uses it to import resources automatically. See [Resource Import](/import-tool).

The game project excludes the host folders from its default compile globs, so
the game and the hosts do not compile each other's source files.

## What the Godot Editor Sees

Open `MyGame/` in the Godot .NET editor as usual, or from a terminal:

::: code-group

```powershell [Windows]
Godot_v:godot-version:-stable_mono_win64.exe --path MyGame --editor
```

```bash [macOS]
Godot_mono.app/Contents/MacOS/Godot --path MyGame --editor
```

```bash [Linux]
godot-mono --path MyGame --editor
```

:::

The hosts' `.gdignore` files hide them from the editor, importer, and exporter.

## How You Get This Layout

For an existing project, the tool adds the hosts around your current files:

```bash
cd path/to/MyGame && dnx 2dog add
```

For a fresh project, the same tool creates the game and hosts together:

```bash
dnx 2dog new MyGame
```

See [`2dog add`](/cli/add) or [`2dog new`](/cli/new) to get started,
and the [CLI reference](/dnx-2dog) for all options.

## When to Use Another Layout

An existing .NET application can host a Godot project elsewhere. Use the
standard layout when you can. If the structure of an existing repository or
application gives you a concrete reason, use a different layout.

For one minimal alternative, see the
[Parallel xUnit Collections Demo](https://github.com/outfox/2dog/tree/main/demos/xunit/parallel_collections),
which uses a small Godot project stub beside MSBuild projects with native dependencies.
