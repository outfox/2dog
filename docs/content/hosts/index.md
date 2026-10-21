---
title: What's a Host?
description: "Choose a .NET application that starts Godot for your game or tests."
tagline: "A host is the .NET app that runs your Godot game, its tests, or your tools."
---

# Hosts

A host is a .NET application that starts Godot for your game or its tests.
Each host has its own entry point and references your base game project.

You can have several hosts of the same type, each with its own project.

The host is what runs your game...

![Illus. Diego Rago: a white anthro dog in a hacker hoodie getting pulled on the leash by their blue robot dog](../img/2dog-squirrel.png)
*(but no worries, you can be totally in charge!)*

## Add a Host

Run this from the directory containing `project.godot`:

```bash
dnx 2dog add
```

Choose hosts when prompted, or use the flags below. For a new project, use
`dnx 2dog new MyGame` instead.

Without host flags, unattended commands include generic .NET and Web.
See [Project Layout](/project-layout) for files and
[MSBuild Configuration](/configuration) for shared settings.

### Console

| Host                             | Flag        | Purpose                                         |
|----------------------------------|-------------|-------------------------------------------------|
| [2dog (generic .NET)](./generic) | `--generic` | .NET console application (desktop or headless)  |
| [REPL](./repl)                   | `--repl`    | Interactive C# inside the running game (opt-in) |

### Desktop

| Host                   | Flag         | Purpose                                     |
|------------------------|--------------|---------------------------------------------|
| [Avalonia](./avalonia) | `--avalonia` | Cross-platform desktop UI                   |
| [Qt Quick](./qt)       | `--qt`       | QML UI with Qt Bridge for C# (experimental) |
| [WinForms](./winforms) | `--winforms` | Windows Forms UI                            |
| [WinUI 3](./winui)     | `--winui`    | Windows App SDK UI                          |

### Web

| Host               | Flag       | Purpose                                                           |
|--------------------|------------|-------------------------------------------------------------------|
| [Blazor](./blazor) | `--blazor` | Game inside a Blazor page                                         |
| [Web](./web)       | `--web`    | WASM / HTML5 browser bundle (e.g. for [itch.io](https://itch.io)) |
| [WebXR](./webxr)   | `--webxr`  | Browser VR and AR                                                 |

### Mobile

| Host                 | Flag        | Purpose                                            |
|----------------------|-------------|----------------------------------------------------|
| [Android](./android) | `--android` | Android APK (experimental)                         |
| [MAUI](./maui)       | `--maui`    | Windows and Android UI with an embedded Godot view |

### Testing

| Host               | Flag       | Purpose                                        |
|--------------------|------------|------------------------------------------------|
| [GdUnit](./gdunit) | `--gdunit` | GDScript suites through embedded Godot and MTP |
| [NUnit](./nunit)   | `--nunit`  | NUnit tests using the Godot engine (opt-in)    |
| [xUnit](./xunit)   | `--xunit`  | xUnit tests using the Godot engine (opt-in)    |
