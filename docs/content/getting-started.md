# Let's Take `Godot` for Walkies 🦴

This guide takes an existing or new Godot C# project through the same first
journey: run it as a .NET application, meet the project layout, test it, and
prepare it for the web. No engine internals or lifecycle code required yet.

## Before You Grab the Leash

You need:

- .NET SDK 10.0 or later
- A supported platform: `win-x64`, `linux-x64`, or `osx-arm64`
- Godot .NET only when you want to edit scenes in the editor UI

2dog imports assets automatically during `dotnet build`, so installing the
Godot editor is not a prerequisite for builds or CI.

## 1. Choose Your Starting Point

Both routes create the same recommended structure. Converting is the shortest
path for an existing Godot C# developer; the template is there when you want a
fresh project.

::: code-group

```bash [🐕 Existing Project]
# Convert in place. Existing game content stays where it is.
dnx 2dog convert path/to/MyGame
cd path/to/MyGame
```

```bash [🌱 Fresh Project]
# Register the template once, then create the project.
dotnet new install 2dog
dotnet new 2dog -n MyGame
cd MyGame
```

:::

::: tip Existing project?
Run `dnx 2dog convert path/to/MyGame --dry-run` first if you want to inspect
every planned action. The [conversion guide](/convert) documents exactly what
the command creates and patches.
:::

## 2. Run the Desktop Host

```bash
dotnet run --project MyGame.2dog
```

::: info Trail marker
You should now see your configured Godot main scene. A fresh project shows the
sample scene; a converted project runs the main scene already configured in
`project.godot`.
:::

This is still your Godot game. The difference is that `MyGame.2dog` is now the
process entry point and starts Godot as an embedded library.

## 3. Meet the Pack

Your Godot project is also the solution root. Three small host projects sit
inside it:

```text
MyGame/                       Godot project and solution root
├── project.godot
├── MyGame.csproj             Scenes and C# game scripts
├── MyGame.2dog/              Desktop host
├── MyGame.web/               Browser host
└── MyGame.tests/             xUnit host
```

Each host folder contains `.gdignore`, so it remains invisible to the Godot
editor, importer, and exporter. Your scenes, scripts, and assets stay at the
root where Godot expects them.

Read [The Recommended Project Layout](/project-layout) for the complete tour
and the responsibility of each layer.

## 4. Keep Using Godot

Open the same project root in the Godot .NET editor:

```bash
godot-mono --editor .
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

Install the WebAssembly build tools once:

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
The same C# Godot project now runs through desktop, test, and browser hosts.
You did not create a second game or port its scripts to another language.
:::

The [Web / Browser guide](/web) covers the development loop, deployment,
configuration, and current browser limitations.

## What You Just Built

- A regular Godot C# project that still opens in the editor
- A desktop application hosted by .NET
- A headless xUnit test project
- A browser host that publishes a static WebAssembly site
- An incremental asset-import step shared by those hosts

The mechanism underneath all of this is small but powerful: your .NET process
owns Godot's lifecycle. [Core Concepts](/concepts) is the next stop when you
want to see that code and understand the main loop.

## Choose the Next Trail

- Learn exactly [what conversion changes](/convert).
- Understand [the recommended project layout](/project-layout).
- Write a useful [scene test with xUnit](/testing).
- Tune the [browser build and deployment](/web).
- Learn about [native build variants](/build-configurations).
- Look up [MSBuild configuration properties](/configuration).

## Package Version Note

Package versions begin with the embedded Godot version. If you add
`2dog.engine` manually, pin it to your project's Godot line so NuGet does not
silently select a newer engine line:

```xml
<PackageReference Include="2dog.engine" Version=":godot-version:.*"/>
```

Generated and converted projects configure their package versions for you.
