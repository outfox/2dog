---
title: What's a Host?
description: "A host is a small .NET program that owns the process, starts embedded Godot, and drives its frame loop - an overview of the hosts 2dog generates and how they relate."
---

# Hosts

A **host** is a small .NET program that owns the process, starts embedded
Godot, and drives its frame loop.

## Adding 2dog Hosts to your Game

[`2dog new`](/templates) generates a new project with your selection of hosts;
[`2dog add`](/add) adds them to an existing project.

Besides the default set, both commands can add a Windows-only
[WinForms host](./winforms) (`--winforms`) that embeds the engine inside a GUI
framework window. It's a good example how to modify the generic host to do
something special.

## Shared Project Anatomy

Every host is an ordinary `Microsoft.NET.Sdk` project with:

- a package reference to its 2dog host package;
- a `ProjectReference` to `MyGame.csproj`, the Godot C# assembly;
- `<GodotProjectDir>`, which points to `project.godot` and is available to
  `Engine.ResolveProjectDir()` at runtime;
- a `.gdignore`, because hosts nest inside the Godot project;
- a `TwoDogVariant` of `release`, `debug`, or `editor` where applicable.

The generic form is the baseline:

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
