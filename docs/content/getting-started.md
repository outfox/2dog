# Let's Take `Godot` for Walkies :gd-bone@gold:

This guide takes an existing or new Godot C# project through the same first
journey: run it as a .NET application, meet the project layout, test it, and
prepare it for the web. No engine internals or lifecycle code required yet.

## Before You Grab the Leash

You need:

- .NET SDK 10.0 or later, [official download](https://dotnet.microsoft.com/download) here.
- A supported platform to develop on: `win-x64`, `linux-x64`, or `osx-arm64`
- The Godot 4.7.x .NET editor, [official download](https://godotengine.org/download), usually needed for game development, but with 2dog it's *technically* optional. Which is the *best kind* of optional.

2dog imports assets automatically during `dotnet build`, so installing the
Godot editor is not a prerequisite for builds or CI.

::: info Trail marker
With 2dog, the same C# Godot project runs through desktop, test, and browser hosts.
It doesn't not create a second game or port its scripts to another language.
GDscript, Autoloads, Input Actions etc. retain their functionality.

It's good old Godot.
:::

## 1. Choose Your Starting Point

Both routes create the same recommended structure. Converting is the shortest
path for an existing Godot C# developer; the template is there when you want a
fresh project.

::: code-group

```bash [Existing Project]
# Convert in place. Existing game content stays where it is.
cd path/to/MyGame
dnx 2dog
```

```bash [Fresh Project]
# Create the Godot project and its hosts in one go.
dnx 2dog new MyGame
cd MyGame
```

:::

The tool asks which hosts you want, shows you the plan, and does nothing until
you confirm. Every question also has a flag  –  `dnx 2dog new MyGame --desktop
--tests` skips straight past the prompts.

::: tip Try before you bite?
Run `dnx 2dog convert path/to/MyGame --dry-run` first if you want to inspect
every planned action. The [conversion guide](/convert) documents exactly what
the command creates and patches.
:::

## 2. Run the Desktop Host

```bash
dotnet run --project MyGame.2dog
```

::: info Trail marker
This is still your Godot game. The difference is that `MyGame.2dog` is now the
process entry point and starts Godot as an embedded library.
:::

## 3. Meet the Pack

Your Godot project is also the solution root. Three small host projects sit
inside it:

```text
MyGame/                       Godot project and solution root
├── project.godot             Scenes, scripts, assets, settings
├── MyGame.csproj             Godot C# game assembly
├── MyGame.2dog/              Desktop host
├── MyGame.web/               Browser host
└── MyGame.tests/             xUnit host
```

Each host folder contains `.gdignore`, so it remains invisible to the Godot
editor, importer, and exporter. Your scenes, scripts, and assets stay at the
root where Godot expects them.

Read the recommended [Project Layout](/project-layout) for the complete tour
and the responsibility of each layer.

## 4. Keep Using Godot

Open the same project root in the Godot .NET editor:

```bash
godot-mono --editor . # or Godot_v4.7.1-stable_mono_win64.exe, etc.
```

Edit a scene or C# script as usual. The next `dotnet build`, `dotnet run`, or
`dotnet test` detects changed project inputs and performs the required Godot
resource import automatically.

::: info Trail marker
You now have two compatible ways into the same project: the Godot editor for
authoring and the .NET hosts for running, testing, and publishing.
:::

## 5. Run the Tests

Generated and converted projects include a headless xUnit host by default:

```bash
dotnet test MyGame.tests
```

This starts Godot without a window, loads the project, and runs tests through
the normal .NET test runner. The supplied fixture also handles Godot's
one-instance-per-process constraint for you.

Continue with [Testing with xUnit](/testing) when you are ready to load your
own scenes and assert game behavior.

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
with any static file server; for example:

```bash
dotnet serve --directory MyGame.web/AppBundle
```

::: info Trail marker
The [Web / Browser guide](/web) covers the development loop, deployment,
configuration, and current browser limitations.
:::


## Package Version Note

Package versions begin with the embedded Godot version. If you add
`2dog.engine` manually, pin it to your project's Godot line so NuGet does not
silently select a newer engine line:

```xml
<PackageReference Include="2dog.engine" Version=":godot-version:.*"/>
```

Generated and converted projects configure their package versions for you.
