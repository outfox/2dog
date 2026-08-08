---
title: Generic Host
description: "The generic 2dog console host: about 20 lines of Program.cs that start the engine, run your main scene, and pump frames - plus headless runs and desktop publishing."
---

# Generic Host

`MyGame.2dog` is a small .NET console application that starts your main scene
and pumps frames until Godot asks to quit.

```bash
dotnet run --project "MyGame.2dog"
```

Edit it like any other .NET application. Run `dnx 2dog add` again to add
another host.

## Program

With no path, `Engine` finds the raw project during development and the
exe-adjacent `.pck` after publish.

```csharp
using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var engine = new Engine("MyGame", args: args);
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

On Windows, `[STAThread]` keeps drag and drop, IME, and native dialogs working.


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
    "MyGame", args: ["--headless", "--audio-driver", "Dummy"]);
```

## Publishing

```bash
dotnet publish MyGame.2dog -c Release
```

The output includes the selected native engine, GodotSharp assemblies, your
game assembly, and your game content exported as `MyGame.2dog.pck` next to the
executable, producing a relocatable build. RID-specific and RID-less publishes
are supported.

The pack is exported through the Godot export preset matching the publish
target: `Windows Desktop`, `Linux`, or `macOS` (the host OS when publishing
without a RID). Projects scaffolded or converted by 2dog ship these presets;
for others, add them in the Godot editor (Project > Export > Add) or set
`<TwoDogDesktopExportPreset>` to an existing preset name. Set
`<TwoDogExportPack>false</TwoDogExportPack>` to skip the export - the publish
output then runs only where the raw project directory exists.

::: warning Unsupported publish modes
`PublishAot` and `PublishSingleFile` fail the build by design: the engine
loads GodotPlugins and the game assembly through hostfxr and on-disk
assemblies, which those publish modes do not provide. Valid configurations are
`Debug`, `Release`, and `Editor` - Godot-side names like `template_release`
are not .NET configurations.
:::

A normal host supports one active engine at a time. Calling `.Dispose()` on the `Engine` object allows a
sequential restart; see [Single Godot Instance](/known-issues/single-instance).
