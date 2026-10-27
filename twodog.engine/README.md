# 2dog.engine

Run the Godot engine inside your .NET application. 2dog loads Godot as a library, so your own `Main` starts it,
drives its frames, and uses the full GodotSharp API.

Most projects never reference this package by hand: the [`2dog`](https://www.nuget.org/packages/2dog) tool
scaffolds hosts that already use it. See [2dog.dev](https://2dog.dev) for the full documentation.

## Getting started

From the folder with your `project.godot`, with the .NET 10 SDK:

```bash
dnx 2dog add                       # or: dnx 2dog new MyGame
dotnet run --project MyGame.2dog
```

The generated desktop host boils down to this:

```csharp
using twodog;

using var engine = new Engine("MyGame", args: args);
engine.Start();

while (engine.Update())
{
    // Your code runs here every frame.
}
```

During development the engine loads your Godot project folder; after `dotnet publish` it loads the exported `.pck`
beside the executable. Call Godot only from the thread that called `Start()`. Lifecycle, restarts and browser
hosting are covered in the [`Engine` reference](https://2dog.dev/api/engine).

## What's included

- **twodog.dll**: the `Engine` hosting API, and the `twodog.Testing` fixtures used by
  [2dog.xunit](https://www.nuget.org/packages/2dog.xunit) and [2dog.nunit](https://www.nuget.org/packages/2dog.nunit)
- **[2dog.godotsharp](https://www.nuget.org/packages/2dog.godotsharp)**: the matching C# bindings and source
  generator
- **Native Godot libraries** for `win-x64`, `linux-x64` and `osx-arm64`, pulled in as platform packages
- **Build integration**: automatic resource import during the build, and `.pck` export on publish

## Learn more

- [Getting started](https://2dog.dev/getting-started)
- [Hosts](https://2dog.dev/hosts/): desktop, web, UI frameworks, Android and tests
- [Testing](https://2dog.dev/testing), including [simulated input](https://2dog.dev/testing/input)
- [API reference](https://2dog.dev/api-reference)
- [Environment variables](https://2dog.dev/configuration/environment-variables), including lifecycle tracing
- [Troubleshooting](https://2dog.dev/troubleshooting)
