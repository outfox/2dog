# dual-instance spike

Proof that two libgodot instances can run concurrently in one process:
renamed copies of the non-mono gdext DLL (one module per copy) + one
AssemblyLoadContext per instance (per-ALC statics isolate the managed
bindings). See `FINDINGS.md` for results and constraints.

Standalone  –  not part of 2dog.sln or CI. Requires the non-mono native in
`godot/bin` (`uv run python build-godot.py --mono no --no-editor --no-glue
--target template_debug`).

```
dotnet build spikes/dual-instance/dual.host/dual.host.csproj
dotnet run --no-build --project spikes/dual-instance/dual.host -- --stage a   # dual native load
dotnet run --no-build --project spikes/dual-instance/dual.host -- --stage b   # sequential dual boot
dotnet run --no-build --project spikes/dual-instance/dual.host -- --stage c   # concurrent
dotnet run --no-build --project spikes/dual-instance/dual.host -- --stage d   # concurrent stress
```

Exit code 0 required per stage; for b/c/d it also proves both per-ALC
ProcessExit FreeLibrary sweeps ran without the 0xE0464645 fail-fast.
Scratch DLL copies and per-instance project dirs land under
`%TEMP%/dual-instance-spike/<pid>/` (left behind; delete freely).

Layout: `dual.host` (default ALC, no Godot types  –  stages, DLL copies, ALC
creation, reflection bridge) · `dual.driver` (loaded once per instance ALC  – 
boots and exercises one engine) · `project-template` (copied per instance).
