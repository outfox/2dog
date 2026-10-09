---
title: REPL Configuration
description: "Configure a C# REPL host for Debug, Editor and Release Godot natives and JIT publishing."
---

# REPL Configuration

The host in `MyGame.repl/` references `2dog.repl`, your game project, and
`2dog.godotsharp.editor`. The Editor binding reference is restored in every
configuration so switching to Editor does not require another restore.

| Configuration | Native variant |
| --- | --- |
| Debug | `debug` |
| Editor | `editor` |
| Release | `release` |

```bash
dotnet run --project MyGame.repl -c Editor
dotnet run --project MyGame.repl -c Release
dotnet run --project MyGame.repl -- --headless
```

Arguments after `--` are passed to Godot. The global [engine properties](/configuration#properties), including `GodotProjectDir` and `TwoDogVariant`,
apply. The host always uses `OutputType=Exe`, including Release.

## Publishing

The REPL compiles C# at runtime and reads assembly metadata from disk.
`PublishAot`, `PublishTrimmed` and `PublishSingleFile` must be `false`; package
targets reject incompatible settings with an explanation. A regular JIT
folder publish, including the default self-contained publish, is supported.

```bash
dotnet publish MyGame.repl -c Release
```

## Embed the Prompt Yourself

Call `ReplHost.Run` on the main thread, with an unstarted engine and an eagerly
loaded game assembly. It starts, pumps and disposes that engine. Load the game
before startup so Roslyn and Godot share the same managed types:

```csharp
using System.Reflection;
using twodog.Repl;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => ReplHost.Run(
        new twodog.Engine("MyGame", args: args), Assembly.Load("MyGame"));
}
```

The prompt's history lives in `2dog/repl-history` under .NET's local application
data directory. `:reset` clears the scripting session while keeping that
history and the engine alive. See [REPL](/hosts/repl) for threading and controls.
