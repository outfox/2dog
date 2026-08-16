---
title: Spaced Project Names
description: "Whitespace in the game project's .NET name makes dotnet publish silently drop its NuGet dependencies from 2dog hosts - an upstream .NET SDK bug 2dog guards against."
---

# Project Names With Spaces Break Publish

A .NET SDK bug makes `dotnet publish` silently drop a *referenced* project's
transitive NuGet packages whenever that project's restore name contains
whitespace and any reference in the publishing project carries
`Publish=false`. Every 2dog web publish qualifies: `PublishTrimmed` brings in
the implicit `Microsoft.NET.ILLink.Tasks` reference with `PrivateAssets="all"`.

The symptom: the game's own DLL ships, but every NuGet package it references
(a JSON library, a logging framework, ...) is missing from
`AppBundle/_framework`, and the app dies at startup with:

```text
System.IO.FileNotFoundException: Could not load file or assembly
'Newtonsoft.Json, Version=13.0.0.0, ...'
```

Godot sets this trap readily - a Godot project named "Fast Dragon" gets a
generated `Fast Dragon.csproj` with a matching assembly name. Stock Godot
never notices (its export publishes the game as the *root* project, which is
unaffected); 2dog hosts reference the game, which is exactly the broken case.

## The mechanism

NuGet records each project's direct dependencies in `project.assets.json` as
flat strings (`"Fast Dragon >= 1.0.0"`), and the SDK's publish-exclusion
computation parses the name by cutting at the first whitespace - yielding
`Fast`, which matches nothing. The game's whole package subtree is then
treated as unreachable and excluded from publish. The restore name resolves as
`PackageId` → `AssemblyName` → project file name, so all of them must be
space-free.

## What 2dog does

`2dog add`/`convert` refuses to scaffold hosts against a name containing
whitespace and offers to fix it - interactively, or via
[`2dog add --rename NewName`](/add#project-names-with-spaces). The rename
touches the .NET identity only: the csproj file, `[dotnet]
project/assembly_name` in `project.godot`, and the solution reference. Godot's
display name (`config/name`) keeps its spaces.

`2dog new` never creates such names (spaces are stripped, with a notice).

## Fixing an affected project by hand

1. Close the Godot editor.
2. Rename `Fast Dragon.csproj` to `FastDragon.csproj`.
3. Set `project/assembly_name="FastDragon"` in `project.godot`'s `[dotnet]`
   section.
4. Point the solution's entry at the new csproj name.
5. In each existing host csproj, update the `ProjectReference`, any
   `TrimmerRootAssembly`, and the `RootNamespace` that carry the old name.

Adding the game's packages directly to the host csproj also works around the
bug (direct references are never excluded) - but it must be kept in sync with
the game's packages by hand, so prefer the rename.
