---
title: Core Concepts
description: "How 2dog inverts Godot ownership: your .NET host owns the process and drives embedded libgodot, with GodotSharp API access, the main loop, resource paths, and the single-instance rule."
tagline: "An overview of the concepts behind 2dog, Godot in .NET."
---

# Core Concepts

Teaching an old robot new tricks! *(and it won't forget any of the older ones)*

2dog adds small "sidecar" projects called [Hosts](/hosts/) to your solution. These hosts embed Godot, and they let you run your own code before, on top of, or after Godot. To do this, 2dog uses `libgodot`, which became an official part of Godot in version 4.6.


![Illus. Moritz Voss: a white anthro dog in a hacker hoodie and glasses scouting ahead with their blue godot robot dog](img/2dog-scouting.png)
*"It's a companion, not a replacement."*

Your original Godot project stays and works the same. Continue to develop it in the official Godot editor as you like.

## 2dog is Godot, backwards!
However, the sidecar projects let us do things quite differently!

In a traditional Godot application, Godot controls the process lifecycle:

```
Godot Process
→ GodotSharp (.NET)
→ SceneTree → Scripts
```

2dog rolls over and reverses the order:

```
.NET Process 
→ twodog.Engine → Godot (as libgodot) 
→ GodotSharp
→ SceneTree → Scripts
```

Your .NET process controls startup, frames, and shutdown. Godot becomes a
rendering, physics, and audio library that your application drives.

## `libgodot` ... ?!

2dog uses `libgodot`, a shared-library build of Godot Engine. A .NET process loads the library. The library supports direct
P/Invoke calls to native APIs and keeps full access to the managed GodotSharp bindings. It compiles and runs on most platforms.

#### Wait, so Godot could just *do that all this time?*
Almost, but... yay for free and libre open source!

2dog is technically a 'fork' or a 'distro' of Godot. It makes some limited changes to the engine. For example, the engine can stop and start again more cleanly, and it does not have to be the first or primary loader of .NET assemblies. 2dog provides these engine builds as binaries in `.nuget` packages that are easy to install. The [2dog tool](./dnx-2dog) gets all these packages for you. To see what it gets, check its output or use `--dry-run`.

`2dog.engine` adds the native `libgodot` for your platform in three variants:
`debug`, `release` and `editor`. They match the Debug, Release and Editor .NET
configurations. [Build Variants](./build-configurations) is the complete guide.

## Lookin' sharp... GodotSharp ... !

That's the current official C# API for Godot. 
::: info ...but what about ❓
<details><summary><i>🔜 <a href="https://github.com/godotengine/godot-dotnet">Godot .NET</a></i></summary>
Made you look! 😃 Godot's shiny new C# integration is going to come in due time, and it will make many things nicer for game developers. Even some of the moving parts of Godot become simpler, but you will still have a reason to keep 2dog around as a faithful <s>fool</s> tool. 

It still gets us all the hosts, application embedding, custom imports &amp; exports, and most of all, <b>solid unit and integration testing</b> in popular frameworks. And oh, that REPL Reddit had been talking about for a week or a half in November '26...
</details>
:::

Your C# game needs GodotSharp, and 2dog gives it to you. After the engine starts, you can use the full GodotSharp API:

```csharp
using var engine = new Engine("MyGame", args: args);
GodotInstance godot = engine.Start(); // Borrowed compatibility handle.

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

## Wow, I have a Main Loop now?

Unlike traditional Godot, the host explicitly runs Godot frame by frame in its own main loop. The host has a basic loop that works like stock Godot. You can change this loop separately in each host project.

```csharp
while (engine.Update())
{
    // Godot processes physics, rendering, input, and your frame logic here.
    if (someCondition)
        break; // Exit when you decide
}
```

`Engine` owns the running instance. `Update()` returns `false` when Godot
wants to quit, for example when the window closes. `RequestQuit()` asks Godot to stop.
After teardown, `Completion` completes and `Exited` fires on every platform.
Desktop disposal is synchronous; browser disposal is asynchronous.

