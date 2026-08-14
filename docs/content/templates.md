---
title: Creating a New Project
description: "Scaffold a complete 2dog project with 2dog new or dotnet new: pick desktop, web, and test hosts, then run your Godot C# game straight from the .NET CLI."
---

# Creating a New Project

Create and run a complete 2dog project without installing a tool:

```bash
dnx 2dog new MyGame
cd MyGame
dotnet run --project MyGame.2dog
```

`2dog new` asks which hosts to include. Pass `--desktop`, `--web`, `--webxr`,
`--tests`, `--winforms`, `--winui`, `--avalonia`, or `--non-interactive` to skip the
prompts.

Already have a Godot project? Use [`2dog add`](/add) instead. It creates the
same layout around your existing content.

## Using `dotnet new`

The same project template ships in the `2dog` NuGet package:

```bash
dotnet new install 2dog
dotnet new 2dog -n MyGame
cd MyGame
dotnet run --project MyGame.2dog
```

The generated project references the required 2dog packages; there is no
separate SDK to install.

## What You Get

```text
MyGame/                     # Godot project root and solution root
├── project.godot
├── MyGame.csproj           # Godot.NET.Sdk game project
├── MyGame.slnx
├── main.tscn
├── export_presets.cfg
├── global.json             # included with the web host
├── MyGame.2dog/            # generic host
├── MyGame.web/             # browser host (holds TwoDogWebBoot.cs)
├── MyGame.tests/           # xUnit tests
├── .editorconfig
└── .gitignore
```

Each host folder contains `.gdignore`, so the Godot editor, importer, and
exporter skip it. The game project also excludes host sources from its compile
globs.

See [Hosts](/hosts/index) for the shared host contract and each generated host's
anatomy. See [Project Layout](/project-layout) for how the nested projects fit
together.

## Choose Hosts

The full template includes desktop, browser, and test hosts. To leave optional
hosts out, or to opt into the [WebXR host](/hosts/webxr), the Windows-only
[WinForms](/hosts/winforms) and [WinUI 3](/hosts/winui) hosts, or the
cross-platform [Avalonia host](/hosts/avalonia):

```bash
dotnet new 2dog -n MyGame --tests false --web false
dotnet new 2dog -n MyGame --avalonia true
```

| Parameter | Default | Description |
| --- | --- | --- |
| `-n, --name` | required | Project name |
| `--tests` | `true` | Include `MyGame.tests` with xUnit and `2dog.xunit` |
| `--web` | `true` | Include the `MyGame.web` browser host |
| `--webxr` | `false` | Include the `MyGame.webxr` browser host with the [WebXR Layers polyfill](/hosts/webxr) for VR |
| `--winforms` | `false` | Include the `MyGame.winforms` WinForms host (Windows-only at runtime) |
| `--winui` | `false` | Include the `MyGame.winui` WinUI 3 host (Windows-only; builds only on Windows) |
| `--avalonia` | `false` | Include the `MyGame.avalonia` Avalonia host (cross-platform GUI; controls composite over the game) |
| `--skipRestore` | `false` | Skip automatic package restore |
| `--twodogVersion` | current release | 2dog package version |
| `--nativesVersion` | current release | Native platform package version |
| `--godotVersion` | current release | `Godot.NET.Sdk` version |

## Work with the Project

From `MyGame/`:

```bash
dotnet build
dotnet run --project MyGame.2dog
dotnet run --project MyGame.2dog -c Release
```

Use the Godot editor normally; the project root is a regular Godot project.
C# scripts belong in that root and compile into `MyGame.csproj`, which every
host references.

For tests, see [Testing](/testing). The [Browser Host guide](/hosts/web)
covers prerequisites, building, local serving, configuration, and platform
limits. Browser builds require:

```bash
dotnet workload install wasm-tools
dotnet publish MyGame.web
```

The rest of the solution builds without `wasm-tools`; browser publishing is an
explicit step.

## Manage the Template

```bash
dotnet new update
dotnet new uninstall 2dog
```

If `dotnet new 2dog` is not found, check `dotnet new list` and reinstall with
`dotnet new install 2dog`.
