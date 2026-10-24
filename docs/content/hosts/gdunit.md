---
title: GdUnit
description: "Run GDScript GdUnit suites through a .NET test host with embedded Godot."
tagline: "Learn how to run your GdUnit suites with dotnet test."
keywords: "GdUnit4, GDScript unit testing, Godot unit testing"
---

# GdUnit

The GdUnit host is a .NET test executable that runs GDScript suites in a
private copy of your game with embedded libgodot. It supports pure GDScript
games and scenes that contain C# components.

![Illus. Moritz Voss: a white anthro dog in a hacker hoodie and their robot dog sniffing the ground](../img/2dog-investigation.png)

To create the host, run this command from your Godot project directory:

```bash
dnx 2dog add --gdunit
```

You can also select GdUnit in the interactive host picker. The generated host
includes a sample GDScript test and its MTP runner configuration.
See the [GdUnit configuration guide](/configuration/gdunit) for customization.
To run the tests, use this command from your Godot project directory:

```bash
dotnet test MyGame.gdunit
```

The root `global.json` that 2dog creates selects MTP for `dotnet test`. If your own
root `global.json` does not select MTP, run the command from `MyGame.gdunit/`.

Use [GdUnit's official documentation](https://godot-gdunit-labs.github.io/gdUnit4/latest/)
to write tests. [Testing with GdUnit](/testing/gdunit) explains
how 2dog changes the way GdUnit runs and reports tests.
