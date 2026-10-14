---
title: GdUnit
description: "Run GDScript GdUnit suites through a .NET test host with embedded Godot."
---

# GdUnit

The GdUnit host is a .NET test executable that runs GDScript suites in a
private copy of your game with embedded libgodot. It supports pure GDScript
games and scenes containing C# components.

Create the host with the [GdUnit configuration guide](/configuration/gdunit).
CLI scaffolding is planned. Run from its directory:

```bash
cd MyGame.gdunit
dotnet test --project MyGame.gdunit.csproj
```

Use [GdUnit's official documentation](https://godot-gdunit-labs.github.io/gdUnit4/latest/)
to write tests. [Testing with GdUnit](/testing/gdunit) explains the 2dog-specific
execution and reporting differences.
