# Plan: ReferenceCallback hot-path cost (global Gate + per-event ptrcall)

Status: **planned, not started** (deferred from the 2026-07-22 review of PR #30).
Prerequisite safety net exists: `twodog.bindings.tests/RefCountConcurrencyTests.cs`.

## Problem

`InstanceBindings.ReferenceCallback` fires on **every** `RefCounted::reference/unreference`
process-wide, on any thread (Variants holding objects, Resources, threaded
`ResourceLoader`, `WorkerThreadPool`). Each event today costs:

1. one process-wide `lock (Gate)`  –  serializes *all* RefCounted traffic across threads, and
2. one `RefCountedNative.GetReferenceCount` method-bind **ptrcall while holding the lock**
   (`RefCountedNative.cs:30`), so the critical section includes a full engine dispatch.

Single-threaded this is latency; multi-threaded it is a global convoy.

## Constraints the current design encodes (do not lose these)

- **Reentrancy**: `GetOrCreate` holds `Gate`, installs the handle, then calls
  `RefCountedNative.Reference()` which re-enters `ReferenceCallback` on the same thread
  and may reflip the slot's handle (`InstanceBindings.cs`, "Order matters" comment).
  Any replacement lock must be reentrant for this path or the `Reference()` call must be
  restructured to happen outside the slot-protection scope.
- **Authoritative count read**: the flip decision re-reads the engine's atomic refcount
  under the lock every time. A managed mirror (`count += isReference ? 1 : -1`) does NOT
  work: callbacks can be observed out of order relative to the engine's atomic ops, so a
  mirror drifts transiently and mis-orders flips. Keep the authoritative read; make it cheap.
- **Slot lifetime**: `FreeCallback` frees the `BindingSlot` memory under `Gate`. A per-slot
  lock stored in the slot can be destroyed while another thread is about to take it  – 
  unless we rely on the engine contract that reference events never race free *for the
  same object* (racing them is a use-after-free in user code anyway). Verify that contract
  in the fork source before relying on it.
- After the dust settles the invariant must hold: `Strong == (refcount > 1)` for every
  slot with a live attached wrapper.

## Phases

### Phase 0  –  measure (do first, keeps us honest)
- [ ] Microbenchmark: variant-from/dispose churn loop (the `RefCountConcurrencyTests`
      shapes) single-threaded and at 4/8 threads, ops/sec. Record baseline in this file.
- [ ] Optional: contention counter (Monitor.TryEnter fail count) to size the convoy.

### Phase 1  –  cheap count read (low risk, keep Gate)
- [ ] Fork-export a direct atomic refcount getter (plain `refcount.get()` load) as a
      `libgodot_` C entry or extension proc, replacing the `get_reference_count`
      method-bind ptrcall inside the critical section.
- [ ] Fork change ⇒ bump `<NativesRevision>` and dispatch Build Natives on the branch
      before rerunning CI (see branch-CI natives gate note).

### Phase 2  –  shrink the lock (the real win; pick ONE)
- [ ] **Recommended first step: sharded gates.** Hash `slot->ObjectPtr` to N locks.
      Every operation touches exactly one slot ⇒ one shard; `GetOrCreate → Reference`
      reenters the same shard (C# locks are reentrant) so the ordering comment keeps
      working verbatim; `FreeCallback` takes the same shard, so slot destroy stays safe.
      Bounded contention win, minimal semantic change.
- [ ] Only if sharding measures insufficient: per-slot CAS state machine (pack
      strong-flag + generation tag; flip = alloc new GCHandle, CAS, free loser; tag
      defeats GCHandle-value ABA). Requires the engine no-race-with-free contract from
      above, and a rewrite of the reentrancy path. High effort  –  needs its own review.

### Phase 3  –  validation gate (applies to any Phase 2 change)
- [ ] Extend `RefCountConcurrencyTests`: 8–16 threads, mixed shared-object churn +
      thread-local churn + concurrent `GetOrCreate` resurrection of collected wrappers.
- [ ] Run the stress suite under forced GC pressure (periodic `GC.Collect(2)` thread
      during churn)  –  flips interact with handle strength and collection timing.
- [ ] Debug-only invariant sweep after drain: sampled slots satisfy
      `Strong == (count > 1)`.
- [ ] Soak: stress tests looped (×100, Release) locally before merging; keep them fast
      enough that CI still runs one pass per config.

## Acceptance

- Multi-threaded churn throughput improves materially (target ≥4× at 4 threads);
  single-threaded does not regress.
- All existing lifetime/GC tests and the extended stress suite stay green in Debug,
  Release, and on wasm (single-threaded  –  no regression expected, verify build only).

## Explicitly rejected

- Managed refcount mirror (callback-order drift, see constraints).
- Moving flip logic engine-side GodotSharp-style: right long-term shape, but it changes
  the GDExtension binding-callback ABI in our fork for all consumers of the interface  – 
  not worth it while Phases 1–2 are unexplored.
