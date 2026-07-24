# Parallel xUnit Collections

Three xUnit test collections running **in parallel in one test process**, each against its own
isolated Godot engine instance, via `twodog.hosting` and its `EngineInstanceFixture` from
`twodog.hosting.xunit`.

This demo covers:

- One `EngineInstanceFixture` subclass per collection, registered **without**
  `DisableParallelization` (contrast with the classic shared-engine collections, which require it).
- The scenario model: tests submit `IEngineScenario` implementations that run on the instance's
  engine thread inside its load context; only the returned report string crosses back.
- Isolation proof: per-instance `ProjectSettings`, a non-default `AssemblyLoadContext`, and pairwise
  distinct native libgodot copies across the running instances.
- `SourceProjectDir`: the `CopiedProjectCollection` boots from a per-instance copy of the real Godot
  project in `project/` (main scene and all), instead of the generated minimal scratch project.
- Platform gating: tests skip via `EngineHost.IsSupported` where in-process hosting is unavailable
  (currently macOS).

## Run

From the project root (fresh checkouts need built packages first: `uv run poe build-all` in the repository root):

```bash
dotnet test
```

`twodog.hosting` / `twodog.hosting.xunit` are not published as NuGet packages yet, so this demo
references them as projects and must live inside the 2dog repository.

Docs: [Testing – Parallel collections](https://2dog.dev/testing#parallel-collections-one-engine-per-collection)
and [Known Issues – Single Godot Instance](https://2dog.dev/known-issues/single-instance).
