# demo

The repository's demo: a Godot project that is also the solution root, with
the 2dog host projects nested inside it (each hidden from the Godot editor by
a `.gdignore`):

- `demo.csproj` / `project.godot` - the Godot project (scenes, resources, C# scripts)
- `demo.2dog/` - desktop host: `dotnet run --project demo/demo.2dog`
- `demo.web/` - browser (wasm) host: `dotnet publish -c Release` from that folder (needs the wasm-tools workload)

The test suite (`twodog.tests/`, at the repository root) runs against this
project. Assets are imported automatically during build.
