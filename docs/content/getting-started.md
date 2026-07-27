# Let's Take `Godot` for a Walk :gd-bone@gold:

Learn how to embed your new or existing Godot project into a .NET application!

## Before You Grab the Leash

To get set up, you'll need:

- [.NET SDK 10.0 or later](https://dotnet.microsoft.com/download)
- A supported platform to develop on: `win-x64`, `linux-x64`, or `osx-arm64`
- The [Godot 4.7.x .NET editor](https://godotengine.org/download) for normal game authoring (builds and CI do not require it)

::: info Trail marker
With 2dog, the same C# Godot project runs through desktop, test, and browser hosts.
It does not create a second game or port scripts to another language. GDScript,
autoloads, input actions, and the rest of Godot still work. Same dog, new leash.
:::

## 1. Choose Your Starting Point

Add [hosts](/hosts/) around an existing project, or start fresh:

::: code-group

```bash [Existing Project]
# Existing game content stays where it is.
cd path/to/MyGame
dnx 2dog
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

`MyGame.2dog` is the process entry point and starts Godot as an embedded library. It runs as a generic .NET console application, which you may change and extend as you wish.

## 3. Meet the Pack

The Godot project is also the solution root. The generated layout starts like this:

```text
MyGame/              Godot project and solution root
├── MyGame.2dog/     Generic host
├── MyGame.tests/    xUnit host
└── MyGame.web/      Browser host
```

Your scenes, scripts, and assets remain at the root. See [Project Layout](/project-layout)
for the complete tree and each project's responsibilities.

## 4. Keep Using Godot

Open the same project root in the Godot .NET editor:

```bash
godot-mono --editor . # or Godot_v4.7.1-stable_mono_win64.exe, etc.
```

Edit scenes and C# scripts as usual. Builds detect changed project inputs and
run the required resource import automatically. See [Resource Import](/import-tool)
for how it works and how to configure it.

::: info Trail marker
You now have two compatible ways into the same project: the Godot editor for
authoring and the .NET hosts for running, testing, and publishing.
:::

## 5. Run the Tests

Projects scaffolded by 2dog include a headless xUnit host by default:

```bash
dotnet test MyGame.tests
```

This starts Godot without a window and runs tests through the normal .NET test runner. See [Testing with xUnit](/testing) to load scenes and test game behavior, and [Single Godot Instance](/known-issues/single-instance) for parallelism constraints.

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
