---
title: Generic Host
description: "The generic 2dog console host starts Godot, runs your main scene, and gives ordinary .NET code control of the frame loop."
---

# Generic Host

`MyGame.2dog` is a small .NET console application that starts your main scene
and pumps frames until Godot asks to quit. Most 2dog hosts have features and limitations
in common with the generic host.

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

On Windows, annotate `Main()` with `[STAThread]`: OLE features such as drag and drop,
IME, and native dialogs need it to initialize.


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

### GPU Selection

Godot defaults to the discrete GPU. On hybrid-GPU systems, a windowing or UI
library sharing the process (Avalonia, for example) may pick a different
adapter, and GPU resources cannot be shared across adapters. The 2dog engine
adds `--gpu-luid` so a host can steer Godot onto a specific adapter by its OS
identity:

```bash
dotnet run --project MyGame.2dog -- --gpu-luid 1af3e
```

The value is the adapter LUID in hexadecimal (Windows). When no device
matches, Godot falls back to automatic selection; an explicit `--gpu-index`
takes precedence. The [Avalonia host](./avalonia) passes it automatically.

## Publishing

```bash
dotnet publish MyGame.2dog -c Release            # host OS
dotnet publish MyGame.2dog -c Release -r win-x64 # or a specific RID
```

Publishes are self-contained by default (`PublishSelfContained` in the host
project file): the output bundles the .NET runtime and runs on machines
without a .NET installation. Pass `-p:PublishSelfContained=false` for a
smaller framework-dependent build that requires the matching .NET runtime on
the target machine.

Publishing game content requires a matching Godot export preset. Generated
projects include `Windows Desktop`, `Linux`, and `macOS` presets.

`PublishAot` and `PublishSingleFile` are unsupported because Godot loads the
game and plugin assemblies from disk through hostfxr.

## Limitations

- A normal host supports one active engine at a time. Disposing it permits a
  sequential restart; see [Single Godot Instance](/known-issues/single-instance).
