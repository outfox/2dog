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

---

Have a question that isn't answered here? Ask on [Discord](https://discord.gg/GAXdbZCNGT) or [open an issue on GitHub](https://github.com/outfox/2dog/issues).
