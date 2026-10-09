# AGENTS.md

2dog lets .NET load Godot as `libgodot` and drive its main loop. The `godot/` submodule is
`thygrrr/godot`, branch `2dog-<GodotVersion>`. This repo packages the fork's natives and C# bindings,
plus a CLI/template for desktop, web, WebXR, xUnit, NUnit, Avalonia, Blazor, WinForms, WinUI and Android hosts.

Nested `AGENTS.md` files add rules for their folders: `docs/` (documentation site) and `.github/` (workflows
and CI scripts). Read them before working there. Put new folder-specific rules in a nested `AGENTS.md`, which
Claude Code and Codex both read, not in `.claude/rules/`. Do not add `CLAUDE.md` files: by default Claude
Code reads them instead of `AGENTS.md`.

## Repository map

- `twodog.engine/` → `2dog.engine`, assembly `twodog.dll`: engine lifecycle, web/Android glue and
  `twodog.Testing` fixtures.
- `twodog.engine/build/2dog.engine.targets`: GodotPlugins copying, automatic resource import through
  `twodog.import` and editor libgodot/`2dog.tools`, desktop/Android pack export and NativeAOT setup.
  `platforms/twodog.browser-wasm/build/` links `libgodot.a` into `dotnet.native.wasm` and assembles AppBundle.
  Import/export inputs exclude nested `.gdignore` trees.
- `twodog.godotsharp/` and `.editor/`: fork bindings/source generator, exact-pinned to `TwoDogVersion`.
  Targets replace compatible stock Godot.NET.Sdk assets and validate resolved assemblies by content hash.
- `twodog.avalonia/` and `twodog.blazor/`: UI integration; host examples live in `demos/showcase/`.
- `twodog.xunit/`: collections, test framework, frame waits and assertions shipped as compile-in source, because xUnit
  discovers collections in the test assembly. Fixtures live in `2dog.engine`; native warnings/errors fail tests.
- `twodog.nunit/`: NUnit's sequential, single-threaded fixture base and Godot signal/wait assertions.
  `tests/nunit/` runs the template examples and real-engine regression tests through NUnit's adapter.
- `twodog.hosting*`: experimental, unpacked multi-instance hosting with a separate assembly load context
  and physical native library per engine. CWD, environment and signal handlers remain process-global;
  macOS hosting is unsupported.
- `twodog/` → `2dog`: dotnet tool and `dotnet new` template in one package. `templates/twodog/` is the
  shared source for scaffolding; versions come from assembly metadata. CLI commands/checks live in
  `Cli/` and `Doctor/`.

## Engine invariants

- `Engine` owns its instance; `Start()` returns a borrowed `GodotInstance`. Pump and access Godot on the
  thread that called `Start()`. Fixtures preserve that owner thread, including the process main thread
  for windowed macOS fixtures.
- `Iteration()` returns true on quit; await `Completion` before restarting with a new `Engine`. Browser
  teardown is asynchronous.
- Normal `Engine` use allows one active instance per load context.
- Android uses Godot's Activity-owned loop; do not call `Engine.Start()` there.

## Build

Use the .NET 10 SDK selected by `global.json`. Run Python only through `uv`; Poe tasks are in `pyproject.toml`.

```text
uv run poe build-godot   # Full host-platform natives, editor executable and Mono glue
uv run poe build        # Platform packs, then managed packs and restore
uv run poe build-all    # build-godot + build
uv run poe build-local  # Incremental host natives + build; needs one prior full native/glue build
uv run poe build-managed # Bindings, engine, Avalonia, Blazor, xUnit, tool packs and restore
```

Managed packing builds the binding packages first; it requires generated glue from a full native build.
Native binaries/managed APIs are under `godot/bin/`; packages go to `packages/`, with `.packages/` as the
local cache.
`build` force-packs missing non-Android natives as empty stubs to avoid downloading other platforms.

Web natives: `uv run build-godot.py --platform web`; Emscripten must match `EmscriptenVersion` in
`Directory.Build.props`. Android native/APK builds: `platforms/twodog.android/README.md` and Poe's Android tasks.
Workloads are host-specific: Web/WebXR/Blazor need `wasm-tools`; Android needs `android`, an Android SDK and
JDK 17. Desktop/test work should not require either workload. Browser/Android/WinUI hosts are excluded from
plain solution builds.

## Packages and versions

- Tests and showcase hosts consume engine/UI/binding packages through `PackageReference`, not source projects.
  Same-version repacks may leave stale extracted files: remove only `.packages/<id>/<version>` for affected
  packages, then `dotnet restore --force`.
- `TwoDogVariant` defaults to `debug` for Debug, `editor` for Editor, `release` otherwise. Desktop packages
  carry all three; web uses `TwoDogWebVariant`. Web/Android support debug/release only. Stage desktop
  natives through `platforms/common/2dog.native-resolver.targets`; leave GodotSharp's resolver slot alone.
- `GodotVersion` must match `godot/version.py`. `TwoDogRevision` produces managed `TwoDogVersion`;
  `release.yml` bumps it, tags `v<TwoDogVersion>` and dispatches CI. Never reset this revision.
- Platform packages, including `2dog.tools`, are versioned by `NativesVersion` from `NativesRevision`. NuGet
  deployment skips duplicates, so reusing a version silently keeps old content. Reset `NativesRevision` to
  zero when `GodotVersion` changes.
- Godot, native, Avalonia, Windows App SDK and ASP.NET Core versions live in `Directory.Build.props`.
  Keep template placeholders; `SetTemplateDefaults` in `Directory.Build.targets` substitutes a staged copy
  during packing.

## Change checklist

- `godot/` submodule or anything packed from `platforms/`, including build targets: bump `NativesRevision`
  and repack the affected platform packages.
- Engine, UI, binding or xUnit package sources, including `twodog.engine/build/`: repack them, e.g. with
  `uv run poe build-managed`, before testing.
- CLI verbs, options or doctor checks: update `twodog/README.md` and `docs/content/cli/`;
  `twodog.tests/Tool/DocsDriftTests.cs` checks both.
- Unmanaged function-pointer signatures used on wasm: declare each in `twodog.engine/WebTrampolines.cs`;
  `WebTrampolineCoverageTests` checks coverage.
- `templates/twodog/` and code the docs tell users to copy: build with stock .NET and Godot.NET.Sdk; avoid
  fork-only generated APIs. In-repo builds resolve the fork's SDK through `nuget.config`, so verify with a
  project scaffolded outside the repo.

## Verification

```text
dotnet test 2dog.tests.slnf -c Debug                   # Also Release and Editor
dotnet test twodog.tests -c Debug --filter "FullyQualifiedName~EngineRestartTests"
dotnet test tests/windowed-fixture -c Release         # Requires a display
uv run tests/bindings/test_packages.py                # Requires packed bindings
uv run python -m unittest discover -s tests/msbuild -v # Configuration tests need wasm-tools
uv run poe test-android-build                        # No device/workload required
node --test .github/scripts/*.test.mjs                # Web/Blazor JavaScript tests
```

Do not use `--no-incremental` with `2dog.tests.slnf`: cleaning projects sharing platform folders can delete
restore assets. Tool version checks use `System.Version`; `-local.N` prerelease versions are unsupported.
Engine fixtures write Godot's native log into `TWODOG_GODOT_LOG_DIR` when it is set; CI uploads those logs.
Showcase: `dotnet run --project demos/showcase/showcase.2dog`; web: publish `demos/showcase/showcase.web`.

## Conventions

- Keep one solution at each Godot project root; nested `<Name>.<host>/` folders carry `.gdignore`.
  Game assembly names follow `[dotnet] project/assembly_name`. Keep host files out of game compile globs.
- Fix warnings at their source; do not suppress warnings or disable failing tests. Find the root cause of
  a flaky test instead of adding retries.
- Preserve CRLF working-tree endings; Git Bash `sed -i` writes LF, so check `git ls-files --eol` after
  scripted edits. Binary assets use Git LFS except `templates/**`, `logo.png` and `nuget/icon.png`.
- The maintainer and other agents work in this tree concurrently: leave unrelated changes alone and check
  `git log -1` before amending.
- `ci.yml` runs only on `outfox/2dog` and tests three OSes in Debug/Release/Editor before tagged NuGet
  deployment. After pushing, verify that the test jobs ran, not just the overall status.
