# Hosts

A **host** is a small .NET program that owns the process, starts embedded
Godot, and drives its frame loop. This is where .NET holds the leash: your
scenes, resources, and game assembly stay in the Godot project, while the host
decides where and how they run.

| Host | Project | Command | Purpose |
| --- | --- | --- | --- |
| [Generic](./generic) | `MyGame.2dog` | `dotnet run --project MyGame.2dog` | Windowed or headless desktop app |
| [Browser](./web) | `MyGame.web` | `dotnet publish MyGame.web` | Static WebAssembly site |
| [xUnit](./xunit) | `MyGame.tests` | `dotnet test MyGame.tests` | Tests using a real engine and resources |

[`2dog new`](/templates) generates a new project with these hosts;
[`2dog add`](/add) adds them to an existing project. Beyond the templates,
the repository demos include a [WinForms host](./winforms) that embeds the
engine inside a GUI framework window.

## Shared Project Anatomy

Every host is an ordinary `Microsoft.NET.Sdk` project with:

- a package reference to its 2dog host package;
- a `ProjectReference` to `MyGame.csproj`, the Godot C# assembly;
- `<GodotProjectDir>`, which points to `project.godot` and is available to
  `Engine.ResolveProjectDir()` at runtime;
- a `.gdignore`, because hosts nest inside the Godot project;
- a `TwoDogVariant` of `release`, `debug`, or `editor` where applicable.

The desktop form is the baseline:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <GodotProjectDir>..</GodotProjectDir>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="2dog.engine" Version=":2dog-version:"/>
    <ProjectReference Include="../MyGame.csproj"/>
  </ItemGroup>
</Project>
```

Browser and xUnit pages describe only their differences. See
[Project Layout](/project-layout) for the full directory model and
[Choosing a Variant](/build-configurations) for native variants.

## Engine Surface

Full signatures are in the [API Reference](/api-reference).

| Member | Purpose |
| --- | --- |
| `new Engine(name, path, args)` | Configure an engine; arguments reach Godot verbatim |
| `Engine.ResolveProjectDir()` | Read `GodotProjectDir` from assembly metadata |
| `engine.Start()` | Boot Godot and run `run/main_scene` |
| `engine.Tree` | Access the live `SceneTree` and GodotSharp API |
| `instance.Iteration()` | Advance one frame; `true` means Godot wants to quit |
| `engine.Run(perFrame)` | Drive the loop with an optional callback |
| `Engine.RegisterWebPluginsInitializer(ptr)` | Register browser plugins before `Start()` |

A normal host supports one active engine at a time. Sequential restarts work
after disposal; isolated load contexts are an experimental option. See
[Single Godot Instance](/known-issues/single-instance).

Windowed Windows hosts should mark `Main` with `[STAThread]` so OLE features
such as drag and drop, IME, and native dialogs initialize correctly.
