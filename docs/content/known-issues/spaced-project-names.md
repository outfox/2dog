---
title: Spaced Project Names
description: "Whitespace in the game project's .NET name makes dotnet publish silently drop its NuGet dependencies from 2dog hosts - an upstream .NET SDK bug 2dog guards against."
tagline: "Learn why spaces in project names break publishing, and how to avoid it."
---

# Project Names With Spaces Break Publish

A .NET SDK bug makes `dotnet publish` silently drop a *referenced* project's
NuGet packages when that project's name contains a space. Every 2dog web
publish triggers it. The name comes from `PackageId`, `AssemblyName` or the
project file name, so all of them must be space-free.

The symptom is that the publish output contains the game's own DLL, but not its
NuGet packages. Every package that the game references, such as a JSON library
or a logging framework, is missing from `AppBundle/_framework`. The app then
crashes at startup with this error:

```text
System.IO.FileNotFoundException: Could not load file or assembly
'Newtonsoft.Json, Version=13.0.0.0, ...'
```

Godot often creates such names. For a project named "Fast Dragon", Godot
generates `Fast Dragon.csproj` with a matching assembly name. Stock Godot
is not affected, because its export publishes the game as the *root* project.
2dog hosts reference the game, which is exactly the broken case.

## What 2dog does

`2dog add` does not set up hosts for a name that contains
whitespace. It offers to fix the name, either interactively or with
`2dog add --rename NewName`. The rename
changes only the .NET identity: the csproj file, `[dotnet]
project/assembly_name` in `project.godot`, and the solution reference. Godot's
display name (`config/name`) keeps its spaces.

`2dog new` never creates such names. It removes spaces and shows a notice.

## Fixing an affected project by hand

1. Close the Godot editor.
2. Rename `Fast Dragon.csproj` to `FastDragon.csproj`.
3. Set `project/assembly_name="FastDragon"` in `project.godot`'s `[dotnet]`
   section.
4. Point the solution's entry at the new csproj name.
5. In each existing host csproj, update the `ProjectReference`, any
   `TrimmerRootAssembly`, and the `RootNamespace` that carry the old name.

Another workaround is to add the game's packages directly to the host csproj,
because the SDK never excludes direct references. But then you must keep these
references in sync with the game's packages by hand, so prefer the rename.
