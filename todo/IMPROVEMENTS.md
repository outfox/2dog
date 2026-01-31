# Improvements

Potential improvements for the 2dog project, roughly ordered by impact.

## 1. CI test workflow

There's a `build-natives.yml` workflow for building libgodot but no automated test runner. A GitHub Actions workflow that runs `dotnet test` (and `dotnet test -c Editor`) on each push would catch regressions early. The headless fixture already works for CI — just needs the workflow file and a way to get the native libraries (either from the build-natives artifact or checked into the repo/packages).

## 2. Fix the test collection naming

`GodotCollection` and `GodotHeadlessCollection` both use `GodotHeadlessFixture`. The non-headless `GodotFixture` loads from `../project/` (which doesn't exist — should probably be `../game/`). Either:
- Remove `GodotCollection` / `GodotFixture` if rendering tests aren't needed
- Fix `GodotFixture` to point at the right path and actually use it for visual/rendering tests

## 3. More demo/example variety

The single demo loads a scene and runs a loop. The project's value proposition is broader — CI test runners, tool scripts, headless servers, import pipelines. Even small examples showing these patterns would strengthen the docs:
- A headless "server" that ticks physics without rendering
- An Editor-config tool that walks the resource filesystem
- A minimal CI test example (standalone, not referencing the test project)

## 4. Template improvements

The `dotnet new` template is a single basic variant. Additional templates could include:
- A `twodog-test` template that scaffolds a test project with fixture + collection + sample test
- A `twodog-tool` template for Editor-config tool scripts

## 5. Error handling and diagnostics

`Engine.cs` uses `Console.WriteLine` for the "Godot instance destroyed" message and throws bare `InvalidOperationException`. Custom exception types (`GodotInstanceException`, `GodotAlreadyRunningException`) would give consumers better catch targets. A structured logging hook (even just an `Action<string>` callback) would be more library-appropriate than Console.

## 6. Dependabot / dependency management

The `dependabot.yml` only covers devcontainers. Adding NuGet and GitHub Actions update groups would keep dependencies current with minimal effort.

## 7. macOS validation

macOS support is marked WIP. If there's access to a Mac (or a CI runner), even a smoke test proving `dotnet test` passes on macOS arm64 would be valuable. The build-natives workflow already produces the macOS binaries.

## 8. Upstream PR tracking

There are 5 Godot PRs documented in `UPSTREAM-CHANGES.md`. A lightweight tracking mechanism (even just updating that file with PR status/merge dates) would help determine when patches can be dropped from the fork.

## 9. Editor subsystem initialization in libgodot

Editor runtime singletons (`EditorInterface`, `EditorFileSystem`, etc.) are not available when running through libgodot, even with the editor build (`TOOLS_ENABLED`). Accessing `EditorInterface.Singleton` throws `NativeMethodBindNotFoundException` — the native method binds are never registered because the editor subsystem startup path (`EditorNode::_init()` or equivalent) is not invoked by `libgodot_create_godot_instance`.

This means the Editor configuration currently provides compile-time type access and `[Tool]` script execution, but not runtime access to editor APIs. The `--import` flag also doesn't work through libgodot for the same reason (the import pipeline is part of the editor subsystem).

### Investigation areas
- **libgodot entry point**: Does `libgodot_create_godot_instance` intentionally skip editor init, or is it an oversight? Check the startup path in `main/main.cpp` vs the libgodot equivalent.
- **Initialization level**: Our `InitCallback` sets `minimum_initialization_level` to `GDEXTENSION_INITIALIZATION_CORE`. Would `GDEXTENSION_INITIALIZATION_EDITOR` change anything, or is that unrelated to the editor subsystem startup?
- **Godot fork patch**: If the skip is intentional, could a targeted patch to the fork's libgodot entry point optionally run editor subsystem init (e.g. behind a flag or arg)?
- **build-godot.py**: Are we missing a scons flag that would include editor ClassDB registrations in the shared library build?

### Current workaround
The `twodog.import` tool and `poe import` task invoke the standalone editor binary as a subprocess for operations that require the editor subsystem.
