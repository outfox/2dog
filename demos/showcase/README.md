# Showcase

The repository's showcase: a Godot project that is also the solution root, with
the 2dog host projects nested inside it (each hidden from the Godot editor by
a `.gdignore`):

- `showcase.csproj` / `project.godot` - the Godot project (scenes, resources, C# scripts)
- `showcase.2dog/` - desktop host: `dotnet run --project demos/showcase/showcase.2dog`
- `showcase.web/` - browser (wasm) host: `dotnet publish` from that folder (defaults to Release; needs the wasm-tools workload)
- `showcase.winforms/` - Windows-only GUI embedding demo (`--wid`): `dotnet run --project demos/showcase/showcase.winforms`
- `showcase.avalonia/` - cross-platform Avalonia embedding demo (controls composite over the game): `dotnet run --project demos/showcase/showcase.avalonia`

The test suite (`twodog.tests/`, at the repository root) runs against this
project. Assets are imported automatically during build.
