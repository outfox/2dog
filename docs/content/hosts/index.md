# Hosts

A **host** is a small .NET program that owns the process, starts the embedded
Godot engine, and drives the frame loop. This is the end of the leash .NET
holds. Your game  –  scenes, resources, and the C# assembly next to
`project.godot`  –  is untouched; the host only decides where and how it runs.

| Host | Project | Run it with | Purpose |
| --- | --- | --- | --- |
| [Console](./console) | `MyGame.console` | `dotnet run --project MyGame.console` | Windowed or headless desktop app  –  the everyday host |
| [Browser](./web) | `MyGame.web` | `dotnet publish MyGame.web` | Static WebAssembly site, no server code |
| [xUnit](./xunit) | `MyGame.xunit` | `dotnet test MyGame.xunit` | Tests against a real engine and real resources |

All three are generated for you by [`dotnet new 2dog`](/templates) or
[`2dog convert`](/convert). More are coming, or can easily be added by you.

## What Every Host Has in Common

- An ordinary `Microsoft.NET.Sdk` project  –  no custom SDK, no launcher.
- A `ProjectReference` to `MyGame.csproj`, the one Godot C# assembly.
- `<GodotProjectDir>`, embedded as assembly metadata and read back at runtime
  by `Engine.ResolveProjectDir()`.
- A `.gdignore`, because hosts nest inside the Godot project
  ([Project Layout](/project-layout)).
- A `TwoDogVariant`: `release`, `debug`, or `editor`
  ([Choosing a Variant](/build-configurations)).

That is the whole contract  –  about ten lines of csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <!-- The directory containing project.godot; '..' in the standard layout -->
    <GodotProjectDir>..</GodotProjectDir>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog.engine" Version=":2dog-version:"/>
    <ProjectReference Include="../MyGame.csproj"/>
  </ItemGroup>
</Project>
```

## The Engine Surface

Hosts differ only in which of these they call, and what they do between
frames. Full signatures in the [API Reference](/api-reference).

| Member | What it does |
| --- | --- |
| `new Engine(name, path, args)` | Configures an engine. `args` reach Godot verbatim |
| `Engine.ResolveProjectDir()` | Reads `GodotProjectDir` back from assembly metadata |
| `engine.Start()` | Boots Godot, runs `run/main_scene`, returns a `GodotInstance` |
| `engine.Tree` | The live `SceneTree`  –  your way into the whole GodotSharp API |
| `instance.Iteration()` | Advances one frame; `true` means Godot wants to quit |
| `engine.Run(perFrame)` | Drives the loop for you, with an optional per-frame callback |
| `Engine.RegisterWebPluginsInitializer(ptr)` | Browser only  –  call before `Start()` |

## Writing Your Own Host

Anything that can call `Start()` is a host: a build tool, a headless
simulation server, an asset pipeline step, a benchmark harness.

```csharp
using Godot;
using Engine = twodog.Engine;

using var engine = new Engine("tool", Engine.ResolveProjectDir(), "--headless");
using var godot = engine.Start();

GD.Print($"Loaded {engine.Tree.CurrentScene?.Name}");

while (!godot.Iteration())
{
    // your work here
}
```

Two rules apply to all of them:

- **One instance at a time.** Starting a second engine before disposing the
  first throws. Sequential restart works; concurrent engines need the
  experimental hosting in [Single Godot Instance](/known-issues/single-instance).
- **Mark `Main` with `[STAThread]` on Windows.** `godot.exe` runs its main
  thread in a single-threaded apartment; on .NET's default MTA thread, OLE
  features (drag & drop, IME, native dialogs) fail to initialize. No effect
  elsewhere.
