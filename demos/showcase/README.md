# Showcase

The repository's showcase: a Godot project that is also the solution root, with
the 2dog host projects nested inside it (each hidden from the Godot editor by
a `.gdignore`):

- `showcase.csproj` / `project.godot` - the Godot project (scenes, resources, C# scripts)
- `showcase.2dog/` - desktop host: `dotnet run --project demos/showcase/showcase.2dog`
- `showcase.web/` - browser (wasm) host: `dotnet publish` from that folder (defaults to Release; needs the wasm-tools workload)
- `showcase.webxr/` - WebXR browser host (its page ships the WebXR Layers polyfill): same publish flow; VR needs a secure context (localhost or HTTPS)
- `showcase.winforms/` - Windows-only GUI embedding demo (`--wid`): `dotnet run --project demos/showcase/showcase.winforms`
- `showcase.avalonia/` - cross-platform Avalonia embedding demo (controls composite over the game): `dotnet run --project demos/showcase/showcase.avalonia`

The test suite (`twodog.tests/`, at the repository root) runs against this
project. Assets are imported automatically during build.

The web hosts reference the engine from the source checkout (ProjectReference
plus in-repo targets imports). Pass `-p:Official=true` to build against the
published NuGet packages instead (`2dog.engine` + `2dog.browser-wasm`, exactly
like a project scaffolded from the 2dog template), e.g.:

    dotnet publish demos/showcase/showcase.web -p:Official=true
