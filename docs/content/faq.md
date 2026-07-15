# FAQ

## How is 2dog different from godot-dotnet?

They solve opposite problems:

- **[godot-dotnet](https://github.com/godotengine/godot-dotnet)** is about using **.NET in Godot**  –  a new bindings layer for writing native extensions in .NET, eventually replacing GodotSharp.
- **2dog** is about using **Godot in .NET**  –  your application is the host, and Godot runs inside it as a library.

2dog is not a replacement for godot-dotnet (or for GodotSharp  –  it uses GodotSharp under the hood). What 2dog changes is the process structure:

**Classic Godot with C# (godot-mono):**

```
godot-mono            ← the engine is the process, it drives everything
├── loads GodotSharp
│   └── runs your code (very indirectly)
└── loads GDExtensions
```

**2dog:**

```
your .NET application  ← your code is the process, it drives the engine
└── loads libgodot
    ├── loads GodotSharp
    └── loads GDExtensions
```

### When to use which

**godot-dotnet:**
- Write native extensions in .NET
- Use .NET inside Godot

**2dog:**
- Use Godot inside .NET:
  - Run unit tests in your IDE test runner or with `dotnet test`
  - Package a Godot game as a .NET application
  - Embed Godot in a .NET application
- Add .NET code on top of an existing game  –  for testing, benchmarking, analysis, or augmentation

## Will 2dog use godot-dotnet in the future?

Likely yes, once that integration is finished. As of mid-2026, godot-dotnet is still early-stage: it provides the basic plumbing to build something *like* GodotSharp, but does not yet offer what GodotSharp offers today. It's important and complex work worth following, but 2dog stays on GodotSharp until godot-dotnet is a practical replacement.

## Is 2dog a replacement for GodotSharp?

No. 2dog embeds GodotSharp (and Godot's C# source generators) and builds on top of it. Everything you know from Godot C# scripting  –  `GD`, `Node`, `[Export]`, signals  –  works the same; 2dog only inverts who is in charge of the process.

## Why is the library a separate package (`2dog.engine`) instead of part of `2dog`?

NuGet forces the split. The `2dog` package is the dotnet tool and the `dotnet new` template in one (`DotnetTool` and `Template` package types), and a package marked as a dotnet tool cannot also be consumed as a `PackageReference`  –  referencing it fails with error `NU1213`  –  so it can't double as the library. The `PackAsTool` layout also conflicts with the library payload (`build/` targets, embedded API assemblies, import helper), and dotnet tool execution doesn't restore package dependencies, so the tool has to be self-contained. Hence one library package (`2dog.engine`) and one tool + template package (`2dog`), released in lockstep. The `2dog` package carries the template content both as `dotnet new` content and embedded in the tool, so `dotnet new 2dog` and `2dog convert` scaffold identical output  –  see [Converting a Godot Project](/convert).

---

Have a question that isn't answered here? Ask on [Discord](https://discord.gg/GAXdbZCNGT) or [open an issue on GitHub](https://github.com/outfox/2dog/issues).
