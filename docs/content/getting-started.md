---
title: Getting Started
description: "Run Godot inside .NET applications with desktop, web, and test hosts."
---

# Let's take Godot for a walk!

**2dog** embeds **Godot** inside **.NET** applications to help build games, apps, and tools for **desktop**, **web**, and **mobile**. It simplifies build automation, has templates for integration with **UI** frameworks and **web servers**, and running automated **tests** in your IDE.

![a white anthro dog in a hacker hoodie and glasses walking their blue godot robot dog](img/2dog-walkies.webp)

## Who's a good robot?
Things you can do with 2dog may be complex, so we try to make it developer friendly.
::: tip From stock Godot to the browser
With the [.NET 10 SDK](https://dotnet.microsoft.com/download) installed, in the
base directory of your Godot project (e.g. `~/MyGame`), run:

```bash
dnx 2dog add --web
dotnet publish MyGame.web
```

Your app/game and `index.html` are then in `MyGame.web/AppBundle/` - easy!

Upload that to itch.io, your web
space, or serve locally, for example:  
`dnx dotnet-serve -d MyGame.web/AppBundle` or `npx serve MyGame.web/AppBundle`

*(use your project's name, i.e. the one in `project.godot`, in place of `MyGame`)*
:::


## Developing
Just use Godot or your favorite IDE. Building with 2dog uses [MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/walkthrough-using-msbuild), which runs on Windows x64, Linux x64, or macOS ARM64. 

2dog upgrades your `.sln` file to the newer `.slnx` (after making a backup `.sln.old`), which you can open in your IDE or editor of choice. Godot understands both solution formats.

The hosts you add appear as projects inside the solution, where you can extend and debug them as normal .NET projects. Use your IDE or `dotnet` CLI to build/run/debug.


## Editing & Importing
Keep editing scenes and scripts in Godot as usual! Continue using the official
[Godot :godot-version: .NET editor](https://godotengine.org/download) for scene editing, development, and use `Project→Export...` to make classic Godot builds using the familiar export templates.

2dog's Hosts import changed resources automatically, you won't need `godot --import`. (you may still run it, of course)

See [Hosts](/hosts/) for more .NET app types, and [Testing with xUnit](/testing/xunit) for tests.
*(it's awesome!)*


## Updating & Fixing
The project's 2dog package dependencies stay pinned until you update them. Run the latest tool to update or check for problems:

```bash
dnx 2dog update
dnx 2dog doctor
```


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
