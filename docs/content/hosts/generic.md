---
title: Generic Host
description: "The generic 2dog console host: about 20 lines of Program.cs that start the engine, run your main scene, and pump frames - plus headless runs and desktop publishing."
---

# Generic Host

`MyGame.2dog` is a generic .NET console application that fits literally in the palm of your hand - just 20 lines of code in `Program.cs` - very easy to extend.

It starts the engine, runs your main scene in a typical Godot game window, and pumps frames until Godot asks to quit. It acts practically the same as directly running your game with Godot.

```bash
dotnet run --project "MyGame.2dog"
```

You can edit the generic host or change it into a different type of application with your IDE as needed. You can even add multiple hosts with the `2dog` dotnet tool by running it multiple times.

:::info Trail Marker
2dog provides some specialized hosts for your convenience, just check out the sibling docs in this category. Contributions are also welcome!
:::

## Program

This is the most basic entry point. `Engine.ResolveProjectDir()` reads
the host's `<GodotProjectDir>` metadata, so no path is hard-coded. That's useful when you need to point this host at different projects, or move your project to a different directory.

```csharp
using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var engine = new Engine("MyGame", Engine.ResolveProjectDir(), args);
        using var godot = engine.Start();

        if (engine.Tree.CurrentScene is { } scene)
            GD.Print($"2dog is running '{scene.Name}'!");

        while (!godot.Iteration())
        {
            // Your per-frame logic here.
        }
    }
}
```

`Start()` runs `run/main_scene`, and `args` reach Godot unchanged. The `using`
declarations dispose the instance and engine in the required order.

Prefer a callback? `engine.Run(perFrame)` iterates until quit and calls your
delegate once per frame.

On Windows, `[STAThread]` matches `godot.exe` and keeps OLE drag and drop, IME,
and native dialogs working. The modern [Top-level statements](https://learn.microsoft.com/en-us/dotnet/csharp/tutorials/top-level-statements) cannot mark their generated entry point; they are suitable only for hosts that will always be headless, or you must manually instantiate the engine on a thread in a Single-Thraded Apartment (STA).


## Project Details

The shared project file is documented in [Hosts](./). The generated desktop
host adds:

| Setting | Purpose |
| --- | --- |
| `ApplicationManifest` | Enables Windows comctl32 v6 and long paths; ignored elsewhere |
| `TwoDogVariant` | Selects the release, debug, or editor native engine |
| `TwoDogRemoveDuplicateGodotAnalyzers` | Prevents the host and game project from loading the same analyzers twice |

| Configuration | Variant | Native | Use |
| --- | --- | --- | --- |
| `Debug` | `debug` | `template_debug` | Development and engine checks |
| `Release` | `release` | `template_release` | Optimized shipping build |
| `Editor` | `editor` | `editor` | `[Tool]` scripts and editor types |

See [Choosing a Variant](/build-configurations) for details. Setting
`GodotProjectDir` also enables [automatic resource import](/import-tool).

## Headless Runs

Use ordinary Godot arguments after `--`:

```bash
dotnet run --project MyGame.2dog -- --headless --quit-after 300
```

For a permanently headless host, pass the arguments in code:

```csharp
using var engine = new Engine(
    "MyGame", Engine.ResolveProjectDir(), "--headless", "--audio-driver", "Dummy");
```

## Publishing

```bash
dotnet publish MyGame.2dog -c Release
```

The output includes the selected native engine, GodotSharp assemblies, and
your game assembly. RID-specific and RID-less publishes are supported, meaning you can "cross-compile". See also: [.NET RID Catalog](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog).

A normal host supports one active engine at a time. Calling `.Dispose()` on the `Engine` object allows a
sequential restart; see [Single Godot Instance](/known-issues/single-instance).
