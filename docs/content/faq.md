---
title: FAQ
description: "Frequently asked questions about 2dog: how it differs from godot-dotnet, GodotSharp compatibility, separate tool and engine packages, and choosing a tool version."
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

NuGet packages marked as dotnet tools cannot also be consumed through a
`PackageReference`; doing so fails with `NU1213`. Therefore `2dog.engine` is the
library, while `2dog` contains the self-contained tool and template. They are
released together, and both scaffolding routes produce the same output. See
[Adding 2dog to a Project](/add).

## How do I keep dnx from switching to a newer 2dog version?

Specify the tool version with `@`:

```bash
dnx 2dog@:2dog-version: doctor
```

Replace the version with the one you want to keep. This selects that version of
the **2dog CLI**; it can still download it if it is missing locally. It does not
read the project's `TwoDogVersion` property to choose the tool version.

Project dependencies are controlled separately by `Directory.Build.props`.
`global.json` selects SDK versions, not the 2dog CLI or engine package version.
See [running a specific tool version under dnx](/dnx-2dog#under-dnx) for the
alternative syntax and the difference between selecting and printing a version.

---

Have a question that isn't answered here? Ask on [Discord](https://discord.gg/GAXdbZCNGT) or [open an issue on GitHub](https://github.com/outfox/2dog/issues).
