---
title: Generic Host
description: "The generic 2dog console host starts Godot, runs your main scene, and gives ordinary .NET code control of the frame loop."
---

# Generic Host

`MyGame.2dog` is a small .NET console application that starts your main scene
and pumps frames until Godot asks to quit.

## Use It

```bash
dotnet run --project MyGame.2dog
```

Edit it like any other .NET application. It is the best starting point for a
desktop game, headless process, or custom host.

## Capabilities

- Runs windowed or headless on Windows, Linux, and macOS.
- Gives host code access to the scene tree and every frame.
- Supports release, debug, and editor engine variants.
- Produces relocatable desktop output with an adjacent game pack.

## How It Works

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


## Project Setup

The shared project file is documented in [Hosts](./). The generated desktop
host adds:

| Setting | Purpose |
| --- | --- |
| `ApplicationManifest` | Enables Windows comctl32 v6 and long paths; ignored elsewhere |
| `TwoDogVariant` | Selects the release, debug, or editor native engine |
| `TwoDogRemoveDuplicateGodotAnalyzers` | Prevents the host and game project from loading the same analyzers twice |

See [Build Variants](/build-configurations) for the configuration-to-variant
mapping. Setting `GodotProjectDir` also enables
[automatic resource import](/import-tool).

### Headless Runs

Use ordinary Godot arguments after `--`:

```bash
dotnet run --project MyGame.2dog -- --headless --quit-after 300
```

For a permanently headless host, pass the arguments in code:

```csharp
using var engine = new Engine(
    "MyGame", args: ["--headless", "--audio-driver", "Dummy"]);
```

## Limitations

- A normal host supports one active engine at a time. Disposing it permits a
  sequential restart; see [Single Godot Instance](/known-issues/single-instance).
- `PublishAot` and `PublishSingleFile` are unsupported because Godot loads the
  game and plugin assemblies from disk through hostfxr.
- Publishing game content requires a matching Godot export preset. Generated
  projects include `Windows Desktop`, `Linux`, and `macOS` presets.


## Windows

Hosts intended to run on Windows should annotate their `Main()` with `[STAThread]` so OLE features
such as drag and drop, IME, and native dialogs initialize correctly.
