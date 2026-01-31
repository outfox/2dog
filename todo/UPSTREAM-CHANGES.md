# Upstream Godot Changes for 2dog

This document describes the changes 2dog makes to Godot's `modules/mono/` module
(branch `2dog-4.6` vs upstream `4.6-stable`). All changes are confined to
`modules/mono/` — no core engine files are modified.

**Goal:** minimize this patch set over time by upstreaming as much as possible.

---

## Overview

11 files changed, 263 insertions, 47 deletions.

| # | Change | Files | Upstream status |
|---|--------|-------|-----------------|
| 1 | [hostfxr error code fix](#1-hostfxr-error-code-fix) | `error_codes.h`, `gd_mono.cpp` | PR [#115654](https://github.com/godotengine/godot/pull/115654) (draft) |
| 2 | [LIBGODOT_HOSTFXR — .NET runtime discovery for libgodot](#2-libgodot_hostfxr--net-runtime-discovery-for-libgodot) | `SCsub`, `mono_configure.py`, `gd_mono.cpp`, `gd_mono.h`, `coreclr_delegates.h`, `hostfxr.h` | PR [#115519](https://github.com/godotengine/godot/pull/115519) (draft) |
| 3 | [Assembly directory resolution (GODOTSHARP_DIR)](#3-assembly-directory-resolution-godotsharp_dir) | `godotsharp_dirs.cpp` | Not yet submitted |
| 4 | [Alert dialogs replaced with error logging](#4-alert-dialogs-replaced-with-error-logging) | `gd_mono.cpp` | Not yet submitted |
| 5 | [Assembly deduplication in PluginLoadContext](#5-assembly-deduplication-in-pluginloadcontext) | `PluginLoadContext.cs` | Not yet submitted |

Two additional GodotSharp C# changes (`Main.cs` re-initialization guard,
`GodotPlugins.csproj` trimming annotations) were previously carried but are
**not required** for 2dog's current single-instance-per-process design and have
been dropped to minimize the diff.

---

## 1. hostfxr error code fix

**Files:** `thirdparty/error_codes.h` (new), `gd_mono.cpp` (2 lines)

The .NET hosting API defines non-zero success codes:

- `0x00000000` — `Success`
- `0x00000001` — `Success_HostAlreadyInitialized`
- `0x00000002` — `Success_DifferentRuntimeProperties`

Upstream checks `rc != 0`, which incorrectly treats these as failures. This
adds the standard `error_codes.h` from the .NET Foundation (MIT license) and
uses `STATUS_CODE_SUCCEEDED(rc)` (checks `>= 0`) instead.

This is a standalone bug fix independent of 2dog. Without it, libgodot loaded
from an already-running .NET host always fails initialization.

**Upstream PR:** [#115654](https://github.com/godotengine/godot/pull/115654)

---

## 2. LIBGODOT_HOSTFXR — .NET runtime discovery for libgodot

**Files:** `SCsub`, `build_scripts/mono_configure.py`, `mono_gd/gd_mono.cpp`,
`mono_gd/gd_mono.h`, `thirdparty/coreclr_delegates.h`, `thirdparty/hostfxr.h`

This is the core change that makes libgodot+mono work when embedded in a .NET
host application.

### Problem

Stock Godot template builds (debug/release) assume they must bootstrap their
own .NET runtime — either from a bundled self-contained deployment or via
CoreCLR direct loading. When Godot is loaded as a shared library (`libgodot.so`
/ `libgodot.dll`) into a .NET host process, CoreCLR is already running. The
stock code path does not handle this.

The editor build has a separate code path that uses hostfxr to discover the
system .NET SDK, but this is gated behind `#ifdef TOOLS_ENABLED` and
unavailable for template builds.

### Solution

- Define `LIBGODOT_HOSTFXR` for `library_type=shared_library` builds
  (`mono_configure.py`).
- Build `hostfxr_resolver.cpp` and `semver.cpp` for shared library builds, not
  just editor builds (`SCsub`).
- Widen all `#ifdef TOOLS_ENABLED` guards in `gd_mono.cpp` and `gd_mono.h` to
  `#if defined(TOOLS_ENABLED) || defined(LIBGODOT_HOSTFXR)`. This enables the
  hostfxr-based discovery path, the `PluginCallbacks` struct, project assembly
  loading, and unconditional .NET module initialization.
- Use **separate** `hdt_load_assembly` + `hdt_get_function_pointer` runtime
  delegates instead of the combined `hdt_load_assembly_and_get_function_pointer`.
  The combined delegate does not work when CoreCLR is already initialized — this
  is a hard constraint of the .NET hosting API.
- Add the missing delegate types to `coreclr_delegates.h` and enum values to
  `hostfxr.h` (these are standard .NET hosting API definitions that upstream
  Godot simply does not include yet).

### Why it can't be avoided

This is native C++ code inside Godot's .NET module initialization. There is no
way to influence it from the managed (C#) side — the host application's code
runs after this initialization completes. Without these changes, libgodot+mono
fails to initialize its .NET integration entirely.

**Upstream PR:** [#115519](https://github.com/godotengine/godot/pull/115519)

---

## 3. Assembly directory resolution (GODOTSHARP_DIR)

**File:** `godotsharp_dirs.cpp`

### Problem

Godot resolves GodotSharp API assemblies (`GodotSharp.dll`, `GodotPlugins.dll`,
etc.) relative to the executable's directory. When running via `dotnet run` or
`dotnet test`, the executable is `/usr/share/dotnet/dotnet` — not the
application's output directory. Godot looks in the wrong place entirely.

### Solution

Two changes:

1. **`GODOTSHARP_DIR` environment variable** (highest priority) — if set and
   the directory contains `GodotPlugins.dll`, use it as the API assemblies
   directory. The 2dog `Engine.cs` sets this at runtime before creating the
   Godot instance, pointing to the directory containing the host application's
   assemblies.

2. **Flat layout fallback** for non-editor `LIBGODOT_HOSTFXR` builds — when
   `GODOTSHARP_DIR` is not set, fall back to `exe_dir` directly (flat layout)
   instead of the nested `GodotSharp/Api/<Config>/` structure that the editor
   expects.

### Why it can't be avoided

The assembly directory resolution happens in native C++ before any managed code
runs. The 2dog host application cannot intercept or redirect it. The
`GODOTSHARP_DIR` env var is the least invasive mechanism — it's a single
`getenv` check with a clean fallback.

---

## 4. Alert dialogs replaced with error logging

**File:** `gd_mono.cpp` (4 call sites)

All `OS::get_singleton()->alert(...)` calls in the .NET initialization path are
replaced with `ERR_FAIL_MSG(...)` / `ERR_FAIL_V_MSG(...)`.

Modal alert dialogs are inappropriate for an embedded/headless library context:
they block the process, may not have a display to render on, and the error
messages preceding them already contain the same information. The replacement
error messages are more detailed (include paths, specific failure context) and
go through Godot's standard error logging.

This is a good candidate for upstreaming independently — the alerts are
arguably poor UX even in the stock editor, since they precede a crash anyway.

---

## 5. Assembly deduplication in PluginLoadContext

**File:** `glue/GodotSharp/GodotPlugins/PluginLoadContext.cs` (10 lines)

### Problem

When libgodot is embedded in a .NET host, the host application's assembly (the
"game" assembly) is already loaded in the Default `AssemblyLoadContext`. When
Godot initializes, `PluginLoadContext` tries to load the same assembly again
into its own isolated context. This creates two copies of every type — a `Node`
from one context cannot be cast to `Node` from the other. All interop between
the host and Godot breaks.

### Solution

Before loading an assembly, check if one with the same name is already loaded
in `AssemblyLoadContext.Default`. If so, return the existing assembly instead of
loading a duplicate.

```csharp
Assembly? existingAssembly = AssemblyLoadContext.Default.Assemblies
    .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName.Name,
        StringComparison.OrdinalIgnoreCase));
if (existingAssembly != null)
    return existingAssembly;
```

### Why it can't be avoided

The whole point of 2dog is that the .NET host IS the game — game types are
defined in the host assembly, which is always in the Default context. The
`SharedAssemblies` list in stock GodotPlugins only covers `GodotSharp` and
`GodotSharpEditor`, not arbitrary game assemblies. There is no way to register
additional shared assemblies from outside `GodotPlugins`.

This is the **only C# change required** and has no downside for stock Godot —
it simply avoids loading an assembly that is already available.

---

## Dropped Changes

The following changes were previously carried but are not required:

- **`Main.cs` re-initialization guard** — Handles the create → destroy →
  recreate lifecycle by short-circuiting on second initialization. 2dog
  currently enforces single-instance-per-process (`Engine.Start()` throws after
  `Dispose()`), so this code path is never reached. Can be re-added if instance
  re-creation is needed in the future.

- **`GodotPlugins.csproj` trimming annotations** — Marks GodotPlugins as
  trim-compatible. Nice-to-have but not functionally required.
