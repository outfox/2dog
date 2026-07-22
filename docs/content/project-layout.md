# The Recommended Project Layout

2dog strongly recommends one layout: the directory containing `project.godot`
is also the .NET solution root, and each way of running the game is a small
nested host project.

It keeps Godot's view of the project familiar while giving desktop, tests, and
the browser independent .NET entry points.

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

The exact scene and script folders are yours to organize. The important rule
is that the game content stays with `project.godot`, while executable hosts
live in ignored subdirectories.

## One Game Assembly

`MyGame.csproj` remains the Godot C# project. It owns:

- C# game scripts, generated source, and game-assembly configuration
- The assembly Godot loads for your game
- The `Godot.NET.Sdk` configuration
- Browser bootstrap code shared with the web host

`project.godot` and its directory remain the home of scenes, resources, and
assets. The host projects do not duplicate your game. They reference its
assembly and content, then decide where and how it runs.

## Three Ways to Run It

### Desktop Host

`MyGame.2dog` is an ordinary .NET executable. It starts the embedded engine,
points it at the parent Godot project, and drives the main loop.

```bash
dotnet run --project MyGame.2dog
```

### Test Host

`MyGame.tests` is an xUnit project. It starts one Godot instance through the
supplied fixture, normally in headless mode, and lets tests load the real game
assembly and resources.

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

The host projects need to find the Godot project, while the Godot editor must
not mistake host source files for game scripts. Nesting solves the first part;
`.gdignore` solves the second.

Every host points its `GodotProjectDir` property at the parent directory:

```xml
<GodotProjectDir>..</GodotProjectDir>
```

That path is embedded as assembly metadata and used at runtime. It also enables
the automatic resource-import target during builds.

In the other direction, the game project excludes the host folders from its
default .NET compile globs. The two layers can therefore share one repository
without swallowing each other's source files.

## What the Godot Editor Sees

Open `MyGame/` in Godot and it sees the same project content it always has:

```bash
godot-mono --editor .
```

The `.gdignore` files hide `MyGame.2dog`, `MyGame.tests`, and `MyGame.web` from
the editor, importer, and exporter. You can continue authoring scenes and
scripts normally.

## How You Get This Layout

For an existing project, conversion adds the hosts around your current files:

```bash
dnx 2dog convert path/to/MyGame
```

For a fresh project, the template creates the game and hosts together:

```bash
dotnet new install 2dog
dotnet new 2dog -n MyGame
```

Both routes intentionally converge on the same structure. See
[Converting a Godot Project](/convert) for patching behavior or
[Project Templates](/templates) for every generated file and option.

## When to Use Another Layout

An existing .NET application can host a Godot project from another path, but
that is an advanced embedding scenario rather than the recommended starting
point. Keep the standard layout unless an established repository structure or
application boundary gives you a concrete reason not to.

## Next Steps

- [Run, test, and publish the layout](/getting-started).
- [Understand the inverted architecture](/concepts).
- [Configure `GodotProjectDir` and native variants](/configuration).
- [Publish the browser host](/web).
