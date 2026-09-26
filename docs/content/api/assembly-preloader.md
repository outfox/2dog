---
title: twodog.fixture.AssemblyPreloader
description: "API reference for AssemblyPreloader, which preloads game assemblies into the default AssemblyLoadContext before Godot starts."
---

# `twodog.fixture.AssemblyPreloader`

Preloads game assemblies into the default `AssemblyLoadContext` before Godot
starts.

```csharp
public static class AssemblyPreloader
```

**Package:** `2dog.engine`  
**Namespace:** `twodog.fixture`

::: info Advanced API
The supplied fixtures call this automatically. Use it only in a custom host
that references game types directly and does not use `FixtureBase`.
:::

## `PreloadGameAssemblies`

```csharp
public static void PreloadGameAssemblies(string projectPath)
```

Finds compiled game assemblies under the project's
`.godot/mono/temp/bin/{Configuration}` directory and loads them into the default
context. This prevents the same game type from being loaded with a second type
identity in Godot's plugin context.

When the host references the game project, the host's own copy is loaded, so it
always matches the host's build configuration; other configurations' outputs and
leftovers of renamed projects are ignored. Otherwise the first of the `Debug`,
`Release` and `Editor` folders that contains assemblies is loaded.

| Parameter | Description |
| --- | --- |
| `projectPath` | Directory containing `project.godot` |

Call it before `Engine.Start()`. In the browser it returns without doing work.
Discovery and load failures are written to the console instead of thrown; one
assembly that fails to load does not stop the others.

```csharp
var projectDir = twodog.Engine.ResolveProjectDir();
AssemblyPreloader.PreloadGameAssemblies(projectDir);

using var engine = new twodog.Engine("MyGame", args: ["--headless"]);
engine.Start();
```
