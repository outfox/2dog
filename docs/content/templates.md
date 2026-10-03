---
title: Creating a New Project
description: "Create a Godot project with a .NET host and run it."
---

# Creating a New Project

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then run:

```bash
dnx 2dog new MyGame --desktop
cd MyGame
dotnet run --project MyGame.2dog
```

Open `project.godot` in the Godot .NET editor to edit scenes and scripts.
See [Project Layout](/project-layout) for the generated files.

## Choose Hosts

Run `dnx 2dog new MyGame` without flags to choose hosts interactively.
To include the generic .NET and xUnit hosts:

```bash
dnx 2dog new MyGame --desktop --tests
```

See [Getting Started](/getting-started) for browser publishing,
[Hosts](/hosts/) for host types, and [`2dog new`](/cli/new) for all options.
For an existing project, use [`2dog add`](/add).

## Use the .NET Template

The same template is available through `dotnet new`:

```bash
dotnet new install 2dog
dotnet new 2dog -n MyGame --web false
```

This creates generic .NET and xUnit hosts. Omit `--web false` to include Web;
install `wasm-tools` first.
