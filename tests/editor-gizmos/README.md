# Editor gizmo lifetime regression

Regression for [Godot #110790](https://github.com/godotengine/godot/issues/110790).
After building the editor/glue and packing the bindings, run from the repo root:

```powershell
uv run tests/editor-gizmos/test_shutdown.py godot/bin/godot.windows.editor.x86_64.executable.mono.exe
```

Use the corresponding editor executable on other platforms. The runner builds the
C# project, imports it, opens the scene, and checks shutdown. No display is required.

The scene creates 100 nodes spanning Node3D, cameras, lights, meshes, markers,
physics joints, paths, and skeletons. A C# gizmo plugin records the gizmos it draws
without retaining their native references. Managed wrappers remain tracked until
shutdown, after the scene nodes have been deleted.

A passing scene run prints `Gizmo regression: 100 gizmos created.` and exits with
code zero. A zero count means the addon or scene did not load and is not a pass.
The pre-fix Windows fork crashes at shutdown in
`godotsharp_internal_refcounted_disposed` / `DisposablesTracker`.
