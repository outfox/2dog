# Plan: multi-instance hosting (concurrent Godot engines in one process)

Status: **phases 1, 2, 4 (core) complete** on branch `multi-instance`  – 
`dotnet test twodog.hosting.tests` runs parallel engine collections green.
Evidence base: `spikes/dual-instance/`  –  two concurrent gdext instances, all 4
stages PASS incl. stress, zero library changes (see its `FINDINGS.md`).

## Problem

`docs/content/known-issues/single-instance.md` documents concurrency as
impossible in-process. The spike disproved that for the gdext stack: the limit
is per native *module*, and per-ALC statics isolate the entire managed bindings
layer. What's missing is a supported, scalable API  –  today the trick requires
hand-rolled ALCs, renamed DLL copies, and a reflection bridge.

Two consumers, in priority order:

1. **Local apps** that want N live engines (tooling hosts, preview-next-to-main,
   simulation farms).
2. **Tests** that want parallel collections instead of today's
   `DisableParallelization = true` serialization.

## Non-negotiable constraints (from the spike)

- **Godot types cannot cross ALCs.** `Godot.Node` in instance A ≠ instance B ≠
  default ALC. All code touching Godot types must live in an assembly loaded
  *into* the instance's ALC. The orchestrator itself must never reference
  twodog.bindings/gdextension.
- **One physical DLL file per instance** (loader dedupes by canonical path;
  symlinks don't count). Copies are cheap; a pool amortizes them.
- **CWD is process-global and the engine moves it** (`--path` chdir, then the
  user-data logs dir). No host code may rely on CWD once any instance exists.
- **`user://` collides across instances** when projects share `config/name`.
  There is no CLI override in the fork (only the
  `application/config/use_custom_user_dir` project settings).
- Mono/GodotSharp stack is **out of scope**: GodotPlugins binds its
  DllImportResolver to one module handle and the GODOTSHARP_DIR /
  GODOT_PROJECT_ASSEMBLY_DIR handshake is process-global env. Browser too
  (single instance per page by platform).

## Design

### New project: `twodog.hosting` (package id `2dog.hosting`, ns `twodog.Hosting`)

Default-ALC orchestrator. References **nothing Godot**  –  that is what makes its
types shareable across ALCs (instance ALCs fall through to the default-ALC copy
for this one assembly, so its interfaces have consistent identity everywhere).

```csharp
// Contract implemented by app code; executes INSIDE the instance ALC,
// on the instance's dedicated thread. Inside, the programming model is
// exactly today's single-instance twodog: new Engine(...), Start(), Run().
public interface IEngineProgram
{
    int Run(IInstanceContext ctx);   // CoreLib-only surface
}

public interface IInstanceContext
{
    string Tag { get; }
    string ProjectDir { get; }
    string[] Args { get; }
    string NativePath { get; }        // this instance's pooled DLL copy
    // v1 comms: CoreLib delegates registered by the host
    Action<string>? Post { get; }     // instance -> host messages
}

// Host side
public sealed class EngineHost : IDisposable
{
    public EngineInstance Start(InstanceOptions options);       // by assembly path + type name
    public EngineInstance Start<TProgram>(InstanceOptions o);   // convenience, see caveat below
}

public sealed class EngineInstance : IDisposable
{
    public string Tag { get; }
    public Task<int> Completion { get; }   // program's Run() result
    public void RequestQuit();             // cooperative, via a host->instance flag the program polls
}
```

`Start<TProgram>` caveat: `typeof(TProgram)` forces the app Exe's bindings copy
into the default ALC. Harmless (those statics never initialize) but the type
itself is NOT used  –  only its assembly path + full name; the instance ALC loads
its own copy. Document both layouts: single-project (convenient, assemblies
loaded twice) and split instance-assembly project (clean).

Target app shape:

```csharp
var host = new EngineHost();
using var a = host.Start<MyGame>(new() { Tag = "A", ProjectDir = "projA" });
using var b = host.Start<MyGame>(new() { Tag = "B", ProjectDir = "projB" });
await Task.WhenAll(a.Completion, b.Completion);
```

### Internals

- **`InstanceAlc`** (generalize the spike's): non-collectible,
  `AssemblyDependencyResolver` over the program assembly; `Load()` returns null
  (default-ALC fallthrough) for `twodog.hosting` itself plus an opt-in
  `SharedAssemblies` list (user contract assemblies  –  must be Godot-free;
  validate by scanning references and fail fast if they pull in twodog.bindings).
- **Native pool**: copies under
  `%LOCALAPPDATA%/2dog/native-pool/<content-hash>/slot-<n>/libgodot-gdext-<variant>.dll`.
  Slot 0 may be the original file (single-instance keeps today's zero-copy
  behavior). Copies persist across runs; pool grows to max observed concurrency.
- **Boot serialization**: one mutex around instance boot (Load + create + start).
  The spike booted two concurrently without incident, but the CWD dance and
  loader-lock pressure make serialized boots cheap insurance; pumping stays
  fully parallel.
- **Threading**: `Start` spawns one dedicated thread per instance; it activates
  the program type inside the ALC and invokes `Run(ctx)`. Program owns the pump
  loop. Exceptions surface through `Completion`.

### Library changes (small, in `twodog.gdextension`)

1. **Handle-based exit sweep** (removes the distinct-filename constraint):
   `NativeLoader` records the `nint` handle from `NativeLibrary.Load`;
   `RegisterProcessExitSweep` frees that handle in a loop instead of
   re-resolving via `GetModuleHandleW(bare filename)` (`Engine.cs:199-206`).
   Each ALC's sweep then frees exactly its own module regardless of naming.
2. Optional diagnostics: stamp `Engine` with the tag for log prefixes.

No changes to `twodog.bindings`  –  the spike proved it per-ALC-safe as-is.

### `user://` isolation

v1: for host-managed scratch projects (tests, generated instances), the pool
writes `application/config/use_custom_user_dir=true` +
`custom_user_dir_name="2dog-<tag>"` into the *copied* project.godot. For
user-owned projects, document the collision and recommend distinct
`config/name`. Stretch (fork): a `--user-data-dir` argument or a libgodot
create-parameter  –  upstream-worthy, not required for v1.

### Test story (phased honesty)

- **v1  –  scenario model** (new package `2dog.gdextension.xunit` or an addition
  to `2dog.xunit`): each xUnit collection owns an `EngineInstanceFixture`
  (one instance via `EngineHost`); collections parallelize  –  no
  `DisableParallelization`. Tests invoke `fixture.Run<TScenario>()` where
  `TScenario : IEngineProgram` lives in the test assembly (fixture loads the
  test assembly into its ALC); asserts happen on the returned CoreLib data.
  This is a *different authoring model* than inline-Godot-assert tests  –  say so
  in docs. Classic tests stay serialized or go multi-process.
- **v2 (stretch)  –  transparent runner**: custom xUnit v3 test framework that
  discovers in the default ALC but executes each collection inside its
  instance ALC (second copy of the test assembly), marshaling results. Real
  work; only if v1 sees adoption.
- Multi-process test splitting remains the recommended default for plain
  parallel CI throughput (crash isolation, zero authoring change)  –  this
  feature serves tests that *need* in-process multiplicity or shared-process
  fixtures.

## Phases

### Phase 1  –  sweep fix + pool primitives  –  DONE
- [x] Handle-based ProcessExit sweep in `twodog.gdextension` (change 1 above)
- [x] Rerun spike stages b/c against it (`--same-names` flag: same-named copies
      in distinct dirs, exit 0  –  the filename constraint is gone)
- [x] `NativesRevision` untouched (managed-only change); normal `TwoDogRevision` flow

### Phase 2  –  `twodog.hosting`  –  DONE (sample = test pilot)
- [x] Project + `EngineHost`/`EngineInstance`/`InstanceAlc`/native pool/contracts
- [x] Public-API consumer: `twodog.hosting.tests` (dual `Start<T>` app story +
      resident fixtures) took the place of a standalone sample;
      `spikes/dual-instance` stays frozen as evidence
- [ ] Stress: N=4 instances, churn, repeated start/stop cycles (ALC leak size
      per cycle measured and documented)

### Phase 3  –  isolation polish
- [x] `user://` isolation for host-managed project copies
      (`ScratchProject.Create`: distinct config/name for generated projects,
      custom_user_dir patch for copies)
- [ ] Per-instance log capture if cheap (route engine output per instance  – 
      investigate fork logger hook; otherwise document interleaving)
- [ ] CWD hazard documented in package README + docs site

### Phase 4  –  test fixtures  –  fixture DONE, adoption pending
- [x] `EngineInstanceFixture` + scenario runner (`twodog.hosting.xunit` +
      `twodog.hosting.runtime`), parallel-collections pilot green
      (`twodog.hosting.tests`, parallelizeTestCollections=true, 7 tests)
- [ ] Convert one real twodog.bindings.tests collection as a pilot

### Phase 5  –  ship
- [x] Add the twodog.hosting* projects to 2dog.sln (Editor sln config maps them
      to Debug, like other config-less projects; 2dog.tests.slnf untouched - it
      filters the mono-stack test workflow)
- [ ] Package `2dog.hosting` in CI like the other packages
- [ ] Rewrite `docs/content/known-issues/single-instance.md`: single-ALC rule
      stays, concurrent instances now possible via 2dog.hosting (gdext stack)
- [ ] Linux validation (dlopen distinct paths  –  expected to work); macOS
      explicitly deferred (exit-abort preexists single-instance)

### Phase 6  –  subprocess hosting as the default test model (review direction)

Adopted from the 2026-07-23 review: ALCs isolate managed statics, but CWD, env
vars, native crashes, signal/exception handlers, process exit, and stdio remain
process-global and cannot be isolated in-process. Therefore:

- [ ] Worker-process-per-engine mode with typed IPC (reuse the
      IEngineProgram/IEngineScenario contracts; length-prefixed JSON or
      StreamJsonRpc over stdio), surfaced through the same fixture API so test
      authoring is identical in both modes
- [ ] Make subprocess mode the DEFAULT for EngineInstanceFixture; in-process
      becomes the explicit opt-in advanced mode with its process-global
      constraints documented on the API
- [ ] In-process mode remains first-class for apps that genuinely need multiple
      engines in one address space (its actual raison d'etre)

## 2026-07-23 code review  –  outcomes

Findings 1-12 fixed in-tree (finding 13 = phase 5/6 work, tracked above):
start/dispose race + disposed-host throw; bounded Dispose (ShutdownTimeout) +
self-thread deadlock guard for OnMessage callbacks; eager absolute-path capture
at Start; exit sweep skips unloading while the ALC's engine still runs; boot
gate fails closed (TimeoutException); SharedAssemblies validated by metadata
walk against the bindings stack; Booted faults when a program exits without
signaling; work queue gained Close/cancel lifecycle (no forever-pending items);
fixture cleans up on ctor failure and deletes scratch dirs (warning on
failure); resident-resource semantics documented on Dispose; pool key renamed
to identity key + partial-copy eviction + verified cross-process race handling;
scratch projects copy recursively and FORCE the custom user dir. Covered by
LifecycleTests/WorkQueueTests (failure paths run without booting engines).

## Open questions / risks

- **Publish modes**: `AssemblyDependencyResolver` needs deps.json + real files  – 
  no single-file, no trimming of instance assemblies. Fail with a clear error.
- **Memory**: ~70 MB WS per headless debug instance, no page sharing between
  copies. Linear in N; fine for tests/tooling, document for apps.
- **Instance recycling**: non-collectible ALCs leak one managed bindings copy
  per instance *slot*. Mitigation: pool/reuse ALCs per slot (an ALC whose
  engine was disposed can boot a new engine  –  sequential-restart semantics
  inside one ALC are already supported). Collectible ALCs stay a research item
  (UnmanagedCallersOnly pointers pinned native-side).
- **Crash blast radius**: unlike multi-process, one instance's native crash
  kills all. Position honestly against multi-process in docs.
- **SEH/signal handlers**: last-loaded module wins; a crash in A may be
  reported by B's handler. Note in docs; no fix planned.
