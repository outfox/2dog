---
title: FAQ
description: "Frequently asked questions about 2dog: how it differs from godot-dotnet, GodotSharp compatibility, separate tool and engine packages, and browser frame rate caps."
tagline: "Curious about 2dog? Find quick answers to the most common questions."
---

# FAQ

## How is 2dog different from godot-dotnet?

They solve opposite problems:

- **[godot-dotnet](https://github.com/godotengine/godot-dotnet)** puts .NET in Godot through a new bindings layer for native extensions.
- **2dog** puts Godot in .NET: your application hosts the engine as a library.

2dog uses GodotSharp; it changes the process structure:

**Classic Godot with C# (godot-mono):**

```
godot-mono            <- the engine process drives everything
├── loads GodotSharp
│   └── runs your code
└── loads GDExtensions
```

**2dog:**

```
your .NET application  <- your process drives the engine
└── loads libgodot
    ├── loads GodotSharp
    └── loads GDExtensions
```

### When to use which

Use godot-dotnet to write native extensions in .NET. Use 2dog to embed or
package Godot in a .NET application, run tests with `dotnet test`, or add .NET
testing and tooling around an existing game.

## Will 2dog use godot-dotnet in the future?

Likely, when it becomes a practical GodotSharp replacement. As of mid-2026,
godot-dotnet provides early plumbing but not GodotSharp's full capabilities, so
2dog remains on GodotSharp.

## Is 2dog a replacement for GodotSharp?

No. 2dog embeds GodotSharp and Godot's C# source generators. `GD`, `Node`,
`[Export]`, signals, and other C# APIs work as usual; only process ownership changes.

## Why is the library a separate package (`2dog.engine`) instead of part of `2dog`?

A dotnet tool package cannot also be a `PackageReference`
([`NU1213`](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1213)). Therefore `2dog.engine` is the
library, `2dog` is the tool, and `2dog.templates` provides the `dotnet new`
template. They are released together, and both scaffolding routes produce the same output. See
[`2dog add`](/cli/add).

## Why does the browser build never exceed my display's refresh rate?

Browser hosts run Godot's frame loop on `requestAnimationFrame`, which the
browser calls once per display refresh. Godot's vsync setting has no effect on
the web, and `application/run/max_fps` can only lower the rate. The browser
also composites the canvas once per refresh, so extra frames would never reach
the screen.

To measure uncapped performance, lift the browser's own limit:

| Browser      | Setting                                                                                     | Notes                                                                                                     |
|--------------|---------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------|
| Chrome, Edge | Start with `--disable-frame-rate-limit --disable-gpu-vsync`                                 | Close every browser window first, or pass a separate `--user-data-dir`                                    |
| Firefox      | Set `layout.frame_rate` to `0` in `about:config`                                            | `-1` (default) follows vsync, `0` renders as fast as possible, any other value is a fixed rate            |
| Safari       | Turn off Develop > Feature Flags > "Prefer Page Rendering Updates near 60fps", then restart | Raises the 60 fps cap to the display rate, such as 120 Hz on ProMotion screens; there is no uncapped mode |

```bash
chrome --disable-frame-rate-limit --disable-gpu-vsync --user-data-dir=/tmp/2dog-bench http://localhost:8080
```

Use these for benchmarks only, and restore the defaults afterwards: uncapped
rendering keeps the CPU and GPU busy and drains batteries. The Safari setting
appears after enabling "Show features for web developers" in Safari's Advanced
settings.

---

Have a question that isn't answered here? Ask on [Discord](https://discord.gg/GAXdbZCNGT) or [open an issue on GitHub](https://github.com/outfox/2dog/issues).
