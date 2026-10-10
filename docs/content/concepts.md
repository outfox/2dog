---
title: Core Concepts
description: "How 2dog inverts Godot ownership: your .NET host owns the process and drives embedded libgodot, with GodotSharp API access, the main loop, resource paths, and the single-instance rule."
---

# Core Concepts

Teaching an old robot new tricks! *(and it won't forget any of the older ones)*

2dog works by adding small "sidecar" projects called [Hosts](/hosts/) to your solution that do the embedding and offer you to run your own code before, on top of, or after Godot. It uses `libgodot`, which became an official part of Godot in version 4.6, to achieve this.


#### A companion, not a replacement
Your original Godot project stays and works the same. Simply continue developing it using the official Godot editor if you like. 


## 2dog is Godot, backwards!
But the side car projects allow us to do things quite differently!

Traditional Godot applications have Godot control the process lifecycle:

```
Godot Process
→ GodotSharp (.NET)
→ SceneTree → Scripts
```

2dog rolls over:

```
.NET Process 
→ twodog.Engine → Godot (as libgodot) 
→ GodotSharp
→ SceneTree → Scripts
```

Your .NET process controls startup, frames, and shutdown. Godot becomes a
rendering, physics, and audio library that your application drives.

## `libgodot` ... ?!

2dog uses `libgodot`, a shared-library build of Godot Engine. The library is loaded by a .NET process, 
supports direct P/Invoke calls to native APIs, and retains full access to GodotSharp managed bindings. It compiles and runs on most platforms.

#### Wait, co Godot could just *do that all this time?*
Almost, but... yay for free and libre open source!

2dog is technically a 'fork' or a 'distro' of Godot, making some limited changes to the engine to allow it to tear down and start up more cleanly, to be not the first or primary loader of .NET assemblies, and more. It provides these as binaries for developers in easy to install `.nuget` packages. The [2dog tool](./dnx-2dog) grabs all these for us, check its output or do a `--dry-run`.

For example, the native library (`libgodot.dll`, `libgodot.so`, or `libgodot.dylib`) ships in
the `2dog.win-x64`, `2dog.linux-x64`, or `2dog.osx-arm64` NuGet packages, respectively.
`2dog.engine` references the appropriate packages automatically.

Each platform package ships three native variants of `libgodot`: `debug` (assertions and
error checking), `release` (optimized for production), and `editor`
(`TOOLS_ENABLED`, editor APIs, resource import). `TwoDogVariant` follows the
Debug, Release, and Editor .NET configurations by default;
[Build Variants](./build-configurations) is the complete guide.

## Lookin' sharp... GodotSharp ... !

That's the current official C# API for Godot. 
::: note ...but what about ❓
<details><summary><i>🔜 <a href="https://github.com/godotengine/godot-dotnet">Godot .NET</a></i></summary>
Made you look! 😃 Godot's shiny new C# integration is going to come in due time, and it will make many things nicer for game developers. Even some of the moving parts of Godot become simpler, but you will still have a reason to keep 2dog around as a faithful <s>fool</s> tool. 

It still get us all the hosts, application embedding, custom imports &amp; exports, and most of all, <b>solid unit and integration testing</b> in popular frameworks. And oh, that REPL Reddit had been talking about for a week or a half in November '26...
</details>
:::

Your C# game needs GodotSharp, and 2dog has us covered. After application startup, the full GodotSharp API is accessible:

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

Unlike traditional Godot, the host explicitly pumps Godot in its own main loop. It comes with a basic implementation that mimicks stock Godot, and you can extend it in each sidecar host project individually.

```csharp
while (!engine.Iteration())
{
    // Godot processes physics, rendering, input, and your frame logic here.
    if (someCondition)
        break; // Exit when you decide
}
```

`Engine` owns the running instance. `Iteration()` returns `true` when Godot
wants to quit, such as when the window closes; `RequestQuit()` asks it to stop.
After teardown, `Completion` completes and `Exited` fires on every platform.
Desktop disposal is synchronous; browser disposal is asynchronous.

