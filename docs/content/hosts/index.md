---
title: What's a Host?
description: "A host is a small .NET program that owns the process, starts embedded Godot, and drives its frame loop - an overview of the hosts 2dog generates and how they relate."
---

# Hosts

A **host** is a small .NET program that owns the process, starts embedded
Godot, and drives its Main Loop.

## Adding a Host

[`2dog new`](/templates) generates a new project with your selection of hosts;
[`2dog add`](/add) adds them to an existing project.

The default set includes Generic, Browser, and xUnit hosts. WebXR, Avalonia,
WinForms, and WinUI 3 are opt-in. Each host page shows the relevant command.

## Solution / Project Layouts

Every host is its own `Microsoft.NET.Sdk` project with:

- a package reference to its 2dog host package;
- `<ProjectReference>` to `MyGame.csproj`, your Godot C# assembly;
- `<GodotProjectDir>`, which points to the location of `project.godot`
- `.gdignore`, because hosts nest inside the Godot project;


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
