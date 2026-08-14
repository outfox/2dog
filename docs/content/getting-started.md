---
title: Getting Started
description: "Embed a new or existing Godot C# project in a .NET application: install the .NET 10 SDK, scaffold hosts with 2dog new or 2dog add, and run your game with dotnet run."
---

# Getting Started / Quickstart


::: info Trail marker: *Same dog, new tricks!*
Once you embed your Godot C# project into .NET applications via 2dog, the project can be run through desktop, test, browser, and other "host" applications.

The stock Godot editor and official export templates will still work, as well.

2dog does not create a second game or port scripts to another language, it just runs a (slightly modified) version of `libgodot` in a .NET process. GDScript, C# Scripts (GodotSharp), autoloads, input actions, and the rest of Godot stay generally the same.
:::

## Prerequisites

To get set up, you'll need:

- the [.NET SDK 10.0 or later](https://dotnet.microsoft.com/download);
- a supported platform to develop on: `win-x64`, `linux-x64`, or `osx-arm64`;
- the official [Godot 4.7.x .NET editor](https://godotengine.org/download) as
  usual, for scene and resource authoring (builds and CI do not require it).


## 1. Let's take Godot for a walk! :gd-bone@gold: 

![a white anthro dog in a hacker hoodie and glasses walking their blue godot robot dog](img/2dog-walkies.webp)

### 2dog works by adding small [host](/hosts/) projects to a Godot project:

::: code-group

```bash [Existing Project]
# Existing game content stays where it is.
cd path/to/MyGame
dnx 2dog add
```

```bash [Fresh Project]
# Create the Godot project and its hosts together.
dnx 2dog new MyGame
cd MyGame
```

:::

The tool asks which hosts you want and shows its plan before making changes.
Flags skip the prompts; for example, `dnx 2dog new MyGame --desktop --tests --web`.

::: tip Try before you bite?
Use `dnx 2dog add path/to/MyGame --dry-run` to inspect every planned action.
[Adding 2dog to a Project](/add) documents what the command creates and patches.
:::

## 2. Run the Generic Host

```bash
dotnet run --project MyGame.2dog
```

`MyGame.2dog` is the process entry point: a plain .NET console application that
starts Godot as an embedded library. Change and extend it like any other .NET
code  –  it's the simplest way to run 2dog, and the first place to look when
troubleshooting.


## 3. Keep Using Godot

Open your project in the Godot .NET editor, it should still work exactly as before:

```bash
godot-mono --editor . # or Godot_v4.7.1-stable_mono_win64.exe, etc.
```

Edit scenes and C# scripts as usual. Builds detect changed project inputs and
run the required resource import automatically. See [Resource Import](/import-tool)
for how it works and how to configure it.

::: info Trail marker
You now have two compatible ways into the same project: the Godot editor for
authoring and exporting, and the .NET hosts for running, testing, and publishing.
:::

## 4. Run some Tests

Projects scaffolded by 2dog include a headless xUnit host by default:

```bash
dotnet test MyGame.tests
```

This starts Godot without a window and runs tests through the normal .NET test runner. See [Testing with xUnit](/testing) to load scenes and test game behavior, and [Single Godot Instance](/known-issues/single-instance) for parallelism constraints.

Start adding some Unit tests right away, we know you want to!


## 5. Publish a Host

```bash
dotnet publish MyGame.2dog -c Release            # host OS
dotnet publish MyGame.2dog -c Release -r win-x64 # or a specific RID
```

The publish folder is a complete build: your host executable, the native
engine, the .NET runtime and assemblies, and your game content exported as
`MyGame.2dog.pck`. Copy the folder to another machine and run the executable  – 
no .NET installation required. See the
[Generic Host guide](/hosts/generic#publishing) for the framework-dependent
opt-out, preset configuration, and unsupported publish modes.



## 6. Publish to the Browser

Install the WebAssembly build tools and a static file server once:

```bash
dotnet workload install wasm-tools
dotnet tool install --global dotnet-serve
```

Then publish the web host:

```bash
dotnet publish MyGame.web
```

The static site is written to `MyGame.web/AppBundle/`. Serve that directory
with any static file server (or upload it to itch.io, S3, etc.)

For example:

```bash
# serve locally
dotnet serve --directory MyGame.web/AppBundle

# release to itch.io
butler push MyGame.web/AppBundle user/game:channel

# upload to AWS
aws s3 sync MyGame.web/AppBundle s3://your-game-bucket

# etc, etc.
```

::: info Trail Marker: Customizing Web Builds
The [Browser Host guide](/hosts/web) covers building, local serving,
deployment, configuration, and current browser limitations.
:::
