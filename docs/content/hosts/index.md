---
title: What's a Host?
description: "A host is a small .NET program that owns the process, starts embedded Godot, and drives its frame loop - an overview of the hosts 2dog generates and how they relate."
---

# Hosts

A **host** is a small .NET program that owns the process, starts embedded
Godot, and drives its frame loop.

## Adding a Host

[`2dog new`](/templates) generates a new project with your selection of hosts;
[`2dog add`](/add) adds them to an existing project.

The default set includes Generic, Browser, and xUnit hosts. WebXR, Avalonia,
and WinForms are opt-in. Each host page shows the relevant command.

## Solution / Project Layouts

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

Hosts talk to Godot through one object: [`twodog.Engine`](/api/engine).
Full signatures are in the [API Reference](/api-reference).

A host runs one engine at a time; sequential restart after disposal works.
See [Single Godot Instance](/known-issues/single-instance).

Windowed Windows hosts should mark `Main` with `[STAThread]` so OLE features
such as drag and drop, IME, and native dialogs initialize correctly.
