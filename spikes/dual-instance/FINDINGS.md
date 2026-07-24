# Dual-instance spike  –  findings

Two libgodot instances CAN run concurrently in one process. The "one instance
per process" limit is per native *module*: a renamed copy of the gdext DLL
loaded twice yields two independent native worlds, and loading the managed
bindings into one AssemblyLoadContext per instance scopes every twodog/Godot
static (proc table, `__mb_*` bind caches, StringNames, InstanceBindings,
`Engine._godotInstancePtr`) to its instance with **zero changes to
twodog.bindings / twodog.gdextension**.

## Verdicts

| Stage | What | Result |
|---|---|---|
| a | Two renamed copies loaded, distinct bases/exports, freed | PASS, exit 0 |
| b | Sequential dual boot (B boots while module A still mapped), both ProcessExit sweeps | PASS, exit 0 (no 0xE0464645) |
| c | Concurrent: both pump 300 frames simultaneously on two threads | PASS, exit 0 |
| d | Concurrent stress: 600 frames × 100 node create/add/free per frame per instance + RefCounted churn + GC pulses | PASS, exit 0 |

Setup: repo @ 2491384, non-mono `godot.windows.template_debug.x86_64.gdext_shared_library.dll`
(v4.7.1.stable.2dog.1456028a5), Windows 11, headless. Concurrency is real:
stage c ran 300+300 frames concurrently in the same ~2.5s wall clock that
stage b needed for 120+120 sequential.

## What worked (no code changes required)

- `Engine.NativePath` → `NativeLoader.LoadExact` injects the per-instance DLL copy cleanly.
- All native calls flow through per-ALC `static delegate*` proc tables populated
  from each instance's own `libgodot_create_godot_instance`  –  no DllImportResolver
  involvement anywhere in the gdext stack.
- The per-ALC single-instance throw never fires; each ALC believes it owns "the" instance.
- Both per-ALC ProcessExit FreeLibrary sweeps ran cleanly  –  **because** the copies
  had distinct file names (see constraints).
- Wrapper identity, RefCounted strong/weak protocol, DisposalQueue drains, and
  finalizer-driven releases all stayed instance-local under concurrent churn.

## Constraints discovered

1. **Distinct file names are mandatory.** The ProcessExit sweep resolves its
   module via `GetModuleHandleW(Path.GetFileName(path))` (twodog.gdextension/Engine.cs:199-206).
   Same-named copies in different directories would make both sweeps free the
   same module. `libgodot-dual-A.dll` / `libgodot-dual-B.dll` keeps them disjoint.
2. **A real copy, not a symlink.** Loaders dedupe by canonical path (and inode
   on Linux); only a byte copy yields a second module.
3. **CWD is process-global and the engine moves it.** Godot chdirs on `--path`
   (stage b: instance B started in A's project dir) and again into the user-data
   logs dir during boot. Nothing broke  –  `res://` access does not depend on CWD
   after boot  –  but host code must never rely on CWD with any engine in-process.
4. **`user://` collides.** Both template projects share `config/name`, so both
   instances wrote the same `%APPDATA%/Godot/app_userdata/<name>/` (observed via
   the CWD trace landing in the shared logs dir). Real parallel use needs distinct
   project names or `--user-dir`-style separation per instance.
5. **Memory cost.** ~70 MB working set per booted headless debug instance
   (21 → 101 → 157 MB across stage b), plus each mapped copy gets no page
   sharing with its sibling (different files).

## Not observed (but not disproven)

- SEH/crash-handler interplay: both modules install handlers; last-wins was not
  exercised (no crash was provoked). A crash in instance A may be reported by
  instance B's handler.
- stdout interleaving stayed line-accurate in these runs (engine writes little
  when headless + quiet); heavy engine logging from both instances may still tear.
- Non-Windows: the whole run is Windows-only so far. On macOS, unloading is
  already impossible (dyld-pinned ObjC images)  –  dual-load there is untested and
  the exit path is expected to be worse, not better.

## Productization delta (if this ever becomes a feature)

- The bindings/gdextension layer is already per-ALC-safe as-is; a single-ALC
  multi-instance design would instead require instance-scoping ~everything
  (proc table, ~all generated `__mb_*` statics, StringNames, InstanceBindings,
  ApiRegistry)  –  the ALC route is drastically cheaper.
- Ship a supported "load exactly this DLL file name" path (exists: `NativePath`)
  plus a documented copy-with-unique-name step, or teach the exit sweep to track
  module handles instead of file names.
- Collectible ALCs (unload an instance's managed world after engine death) were
  deliberately not attempted: UnmanagedCallersOnly pointers remain registered
  native-side for module lifetime. Non-collectible ALCs leak one managed copy of
  the bindings per instance  –  acceptable for test runners, unresolved for
  long-lived hosts that cycle many instances.
- Type identity: `Godot.Node` in ALC-A ≠ ALC-B. Host code can only talk to an
  instance through reflection/CoreLib types (this spike) or a shared interface
  assembly loaded in the default ALC. Fine for test isolation; awkward for an
  API surface.

## Recommendation for parallel tests

Dual-load works, but xUnit would need the same ALC gymnastics for every test
assembly (tests reference Godot types → each collection's tests must live inside
the instance's ALC, invisible to the default-ALC test framework). Multi-process
(split into several test projects; `dotnet test` parallelizes across them) gets
the same parallelism with none of that, plus crash isolation  –  which matters
given the known shutdown fragility. Use dual-load knowledge where in-process
multiplicity is genuinely required (e.g. an editor-tooling host embedding a
preview instance next to a main one), not as the default test strategy.
