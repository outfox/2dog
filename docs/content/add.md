---
title: Adding 2dog to Existing Projects
description: "Add a .NET host to an existing Godot project and run it."
---

# Adding 2dog to Your Project

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then run:

```bash
cd path/to/MyGame
dnx 2dog add --generic
dotnet run --project MyGame.2dog
```

2dog creates a nested host project that runs your game. Keep editing scenes
and scripts in Godot as usual. GDScript projects work too.

## Choose Hosts

Run `dnx 2dog add` without flags to choose hosts interactively, or name them:

```bash
dnx 2dog add --generic --tests
```

Run the command again to add more hosts. Use `--dry-run` to preview changes.
See [Getting Started](/getting-started) for browser publishing,
[Hosts](/hosts/) for host types, and [`2dog add`](/cli/add) for all options.
