---
title: What's a Host?
description: "Choose a .NET application that starts Godot for your game or tests."
---

# Hosts

A host is a .NET application that starts Godot for your game or its tests.
Each host has its own entry point and references the same game project.

## Add a Host

Run this from the directory containing `project.godot`:

```bash
dnx 2dog add
```

Choose hosts when prompted, or use the flags below. For a new project, use
`dnx 2dog new MyGame` instead.

| Host | Flag | Purpose |
| --- | --- | --- |
| [2dog (generic .NET)](./generic) | `--desktop` | Desktop or headless .NET application |
| [Avalonia](./avalonia) | `--avalonia` | Cross-platform desktop UI |
| [Blazor](./blazor) | `--blazor` | Game inside a Blazor page |
| [Web](./web) | `--web` | Static browser site |
| [WebXR](./webxr) | `--webxr` | Browser VR and AR |
| [WinForms](./winforms) | `--winforms` | Windows Forms UI |
| [WinUI 3](./winui) | `--winui` | Windows App SDK UI |
| [xUnit](./xunit) | `--tests` | Tests using the Godot engine |

Without host flags, unattended commands include generic .NET, Web, and xUnit.
See [Project Layout](/project-layout) for files and
[MSBuild Configuration](/configuration) for shared settings.
