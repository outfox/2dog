---
title: What's a Host?
description: "Choose a .NET application that starts Godot for your game or tests."
---

# Hosts

A host is a .NET application that starts Godot for your game or its tests.
Each host has its own entry point and references the same game project.

You can have multiple hosts of the same type, each will have its unique project.


## Add a Host

Run this from the directory containing `project.godot`:

```bash
dnx 2dog add
```

Choose hosts when prompted, or use the flags below. For a new project, use
`dnx 2dog new MyGame` instead.

| Host | Flag | Purpose |
| --- | --- | --- |
| [2dog (generic .NET)](./generic) | `--generic` | .NET console application (desktop or headless) |
| [Android](./android) | `--android` | Android APK (experimental) |
| [Avalonia](./avalonia) | `--avalonia` | Cross-platform desktop UI |
| [Blazor](./blazor) | `--blazor` | Game inside a Blazor page |
| [NUnit](./nunit) | `--nunit` | NUnit tests using the Godot engine (opt-in) |
| [REPL](./repl) | `--repl` | Interactive C# inside the running game (opt-in) |
| [Web](./web) | `--web` | WASM / HTML5 browser bundle (e.g. for [itch.io](https://itch.io))|
| [WebXR](./webxr) | `--webxr` | Browser VR and AR |
| [WinForms](./winforms) | `--winforms` | Windows Forms UI |
| [WinUI 3](./winui) | `--winui` | Windows App SDK UI |
| [xUnit](./xunit) | `--tests` | Tests using the Godot engine |

Without host flags, unattended commands include generic .NET, Web, and xUnit.
See [Project Layout](/project-layout) for files and
[MSBuild Configuration](/configuration) for shared settings.
