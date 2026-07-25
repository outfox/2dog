# Console Host

`MyGame.console` is the everyday host: a .NET console application that starts
the engine, runs your main scene in a window, and pumps frames until Godot
asks to quit. It is what you develop against, what you publish for desktop
players, and the shape every other host follows.

```bash
dotnet run --project MyGame.console
```

## The Program

There is no framework underneath this  –  only your `Main()`. The one piece of
magic is `Engine.ResolveProjectDir()`, which reads the `<GodotProjectDir>`
recorded as assembly metadata at build time, so nothing hard-codes a path.

```csharp
using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    // STA matches how godot.exe runs its main thread on Windows: OLE (drag & drop,
    // IME, native dialogs) fails to initialize on the MTA thread .NET uses by default.
    // No effect on Linux/macOS.
    [STAThread]
    private static void Main(string[] args)
    {
        // Start() runs the main scene configured in project.godot
        // (run/main_scene), exactly like launching godot.exe would.
        // args reach Godot verbatim: --headless, --verbose, --quit-after N, ...
        using var engine = new Engine("MyGame", Engine.ResolveProjectDir(), args);
        using var godot = engine.Start();

        if (engine.Tree.CurrentScene is { } scene)
            GD.Print($"2dog is running '{scene.Name}'!");

        // Iteration() returns true when the engine wants to quit
        // (window closed, SceneTree.Quit(), --quit-after elapsed, ...).
        while (!godot.Iteration())
        {
            // Your per-frame logic here
        }
    }
}
```

::: tip Prefer a callback to a loop?
`engine.Run(perFrame)` iterates until quit and calls your delegate once per
frame. On desktop both forms block until the engine shuts down.
:::

## The Project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RootNamespace>MyGame.Console</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog.engine" Version=":2dog-version:"/>
    <ProjectReference Include="../MyGame.csproj"/>
  </ItemGroup>

  <!-- The Godot project is the parent directory; hosts nest inside it -->
  <PropertyGroup>
    <GodotProjectDir>..</GodotProjectDir>
    <TwoDogVariant Condition="'$(Configuration)' == 'Debug'">debug</TwoDogVariant>
    <TwoDogVariant Condition="'$(Configuration)' == 'Editor'">editor</TwoDogVariant>
    <TwoDogRemoveDuplicateGodotAnalyzers>true</TwoDogRemoveDuplicateGodotAnalyzers>
  </PropertyGroup>
</Project>
```

| Piece | Why it is there |
| --- | --- |
| `2dog.engine` | The host API, plus the native platform package for your OS |
| `ProjectReference` | The game assembly Godot binds scripts against  –  referenced, never duplicated |
| `GodotProjectDir` | Locates `project.godot`; also enables [automatic import](/import-tool) at build time |
| `TwoDogVariant` | Picks `libgodot-debug` / `-release` / `-editor`. Unset means `release` |
| `TwoDogRemoveDuplicateGodotAnalyzers` | Host and `Godot.NET.Sdk` game project would otherwise load the same analyzers twice |
| `app.manifest` | Windows: the comctl32 v6 context Godot's display server needs for native dialogs, plus long-path awareness. Ignored elsewhere |

A `.gdignore` sits beside the csproj, keeping the Godot editor, importer, and
exporter out of the host directory  –  see [Project Layout](/project-layout).

## Build Configurations

| Configuration | Variant | Native | Use it for |
| --- | --- | --- | --- |
| `Debug` | `debug` | `template_debug` | Development  –  assertions and engine error checks |
| `Release` | `release` | `template_release` | Shipping  –  optimized, smallest |
| `Editor` | `editor` | `editor` | `[Tool]` scripts and editor types (`TOOLS_ENABLED`) |

The mapping is just the two `Condition` lines above; change them if your
configurations differ. [Choosing a Variant](/build-configurations) covers what
each one can and cannot do.

## Running Headless

A headless service, CI job, or batch tool is this host with an argument:

```bash
dotnet run --project MyGame.console -- --headless --quit-after 300
```

Or bake it in when the host is only ever headless:

```csharp
using var engine = new Engine("MyGame", Engine.ResolveProjectDir(), "--headless", "--audio-driver", "Dummy");
```

## Driving the Frame Loop

Because the loop is yours, per-frame work does not have to live in a Godot
script. Anything reachable from `engine.Tree` is fair game, and nodes with
`_Process` keep running exactly as before:

```csharp
var cubes = engine.Tree.CurrentScene
    .GetNode<Node3D>("Flair/WhiteCubes")
    .GetChildren().OfType<Node3D>().ToArray();

while (!godot.Iteration())
{
    var delta = (float)engine.Tree.Root.GetProcessDeltaTime();
    foreach (var cube in cubes)
        cube.Rotate(Vector3.Up, 1.8f * delta);
}
```

## Publishing

```bash
dotnet publish MyGame.console -c Release
```

The output carries the selected `libgodot-<variant>`, the GodotSharp
assemblies in the layout that variant expects, and your game assembly. Both
RID-specific and RID-less publishes work.

::: warning Publishing from a source checkout
Building against the 2dog repository rather than the NuGet packages? Run
`dotnet build twodog.import -c Release` once first, so the import helper is
available to the publish.
:::

## Shutdown

The `using` declarations dispose the instance and the engine in the right
order at the end of `Main`. That is also what makes a *sequential restart*
legal: once disposed, a new engine can start in the same process. Starting a
second one first throws  –  see
[Single Godot Instance](/known-issues/single-instance).
