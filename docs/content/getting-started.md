---
title: Getting Started
description: "Run Godot inside .NET applications with desktop, web, and test hosts."
---

# Let's take Godot for a walk!

2dog runs Godot inside .NET applications for desktop games, web publishing,
embedded UIs, and automated tests.

![a white anthro dog in a hacker hoodie and glasses walking their blue godot robot dog](img/2dog-walkies.webp)

::: tip From stock Godot to the browser
With the [.NET 10 SDK](https://dotnet.microsoft.com/download) installed, in the
base directory of your Godot project (e.g. `~/MyGame`), run:

```bash
dnx 2dog add --web
dotnet publish MyGame.web
```

Your app/game and `index.html` are then in `MyGame.web/AppBundle/`. Upload that to itch.io, your web
space, or serve locally, e.g. `dnx dotnet-serve -d MyGame.web/AppBundle`
or `npx serve MyGame.web/AppBundle`.

*(use your project's name in place of `MyGame`)*
:::

## Developing

Develop on Windows x64, Linux x64, or macOS ARM64. 2dog upgrades your `sln` file to `slnx`, so you can code in your IDE or code editor of choice - including Godot itself.

The various hosts appear as projects inside the solution, where you can extend and debug them as normal .NET projects.

You may have any number of hosts of the same type, 2dog will ask you for alternative names on creation. This is great for different automation suites, demo versions, etc.

## Updating and Fixing
The project's package versions stay pinned until you update them. Run the latest tool to update or check for problems:

```bash
dnx 2dog update
dnx 2dog doctor
```

## Importing and Editing
Just continue using the official
[Godot :godot-version: .NET editor](https://godotengine.org/download) for scene editing or other exports.

Keep editing scenes and scripts in Godot as usual. Hosts import changed resources automatically (this differs from other solutions that may need you to run `godot --import`).

See [Hosts](/hosts/) for other host types and [Testing with xUnit](/testing) to begin writing tests... *(you should, it's awesome!)*


## Squirrel ?!

2dog can do a lot more, such as provide unit testing, embedding in other applications and UIs, etc. It's maybe safest to try this in a completely fresh project, and *you know you wanted to start a new game project anyway!*

To create a project from scratch, `project.godot` and all, from the command line:

```bash
dnx 2dog new NewGame
cd NewGame
dotnet run --project NewGame.2dog
dotnet test
godot-mono -e . # or open it in the Godot project picker, etc.
```
