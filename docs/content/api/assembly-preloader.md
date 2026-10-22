---
title: twodog.fixture.AssemblyPreloader
description: "API reference for AssemblyPreloader, which preloads game assemblies into the default AssemblyLoadContext before Godot starts."
tagline: "Learn how to load your game assemblies before Godot starts."
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
context. This stops Godot's plugin context from loading the same game type again with a
second type identity.

When the host references the game project, the preloader loads the host's own
copy, so it always matches the host's build configuration. It ignores the
outputs of other configurations and the leftovers of renamed projects. If the
host does not reference the game project, the preloader loads the first of the
`Debug`, `Release` and `Editor` folders that contains assemblies.

| Parameter     | Description                          |
|---------------|--------------------------------------|
| `projectPath` | Directory containing `project.godot` |

Call it before `Engine.Start()`. In the browser, it returns and does nothing.
It writes discovery and load failures to the console and does not throw them.
If one assembly fails to load, it still loads the others.

```csharp
var projectDir = twodog.Engine.ResolveProjectDir();
AssemblyPreloader.PreloadGameAssemblies(projectDir);

using var engine = new twodog.Engine("MyGame", args: ["--headless"]);
engine.Start();
```
