---
title: Core Concepts
description: "How 2dog inverts Godot ownership: your .NET host owns the process and drives embedded libgodot, with GodotSharp API access, the main loop, resource paths, and the single-instance rule."
---

# Core Concepts

Teaching an old robot new tricks!

## 2dog is Godot, backward!

Traditional Godot applications have Godot control the process lifecycle:

```
Godot Process
→ GodotSharp (.NET)
→ SceneTree → Scripts
```

2dog rolls over:

```
.NET Process 
→ twodog.Engine → Godot (as library) 
→ GodotSharp
→ SceneTree → Scripts
```

Your .NET process controls startup, frames, and shutdown. Godot becomes a
rendering, physics, and audio library that your application drives.

## `libgodot` ... ?!

2dog uses `libgodot`, a shared-library build of Godot Engine. It is loaded by a .NET process, 
supports direct P/Invoke calls to native APIs, and retains full access to GodotSharp managed bindings.

The native library (`libgodot.dll`, `libgodot.so`, or `libgodot.dylib`) ships in
the `2dog.win-x64`, `2dog.linux-x64`, or `2dog.osx-arm64` NuGet package.
`2dog.engine` references the appropriate packages automatically.

Each platform package ships three native variants of `libgodot`: `debug` (assertions and
error checking), `release` (optimized for production), and `editor`
(`TOOLS_ENABLED`, editor APIs, resource import). Generated hosts map the
Debug, Release, and Editor .NET configurations onto them with `TwoDogVariant`;
[Build Variants](./build-configurations) is the complete guide.

## GodotSharp ... !

After startup, the full GodotSharp API is accessible:

```csharp
using var engine = new Engine("MyGame", args: args);
using var godot = engine.Start();

// Access the scene tree
SceneTree tree = engine.Tree;

// Load and instantiate scenes
var scene = GD.Load<PackedScene>("res://my_scene.tscn");
var instance = scene.Instantiate();
tree.Root.AddChild(instance);

// Use any Godot API
var viewport = tree.Root.GetViewport();
var physics = PhysicsServer3D.Singleton;
```

## I have a Main Loop now?

Unlike traditional Godot, the host explicitly pumps Godot in its main loop:

```csharp
while (!godot.Iteration())
{
    // Godot processes physics, rendering, input, and your frame logic here.
    if (someCondition)
        break; // Exit when you decide
}
```

`Iteration()` returns `true` when Godot wants to quit, such as when the window closes.

## Single Instance only (for now.)

Only one Godot instance can run per assembly load context at a time. Sequential
restart is supported. See [Single Godot Instance](./known-issues/single-instance)
for examples and the experimental isolated-hosting path.
