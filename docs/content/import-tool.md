# Resource Import

Godot projects need the editor's resource import pass before they can run: it
generates `.uid` files for C# scripts, imports textures and meshes, and builds
the `.godot/` cache (including the script UID cache). Without it the engine
logs a startup error about missing UIDs.

2dog runs this import **automatically during build**.

## Automatic import (MSBuild)

Any project that sets `<GodotProjectDir>` and references the `2dog.engine` package
gets an incremental import step after `Build`:

- When source files under the Godot project change, the import runs; otherwise
  the target is skipped as up-to-date (tracked via a stamp file in the
  project's `.godot/` directory, shared by all consuming projects).
- Directories containing a `.gdignore` file (such as the nested 2dog host
  projects) are excluded from the change tracking, mirroring the Godot
  importer's own `.gdignore` semantics  –  editing host code never retriggers
  the import. Extra excludes can be added via `TwoDogImportExcludes`.
- No Godot editor installation is required. The import runs in a helper
  process against the **editor-variant libgodot** already shipped in the
  `2dog.<rid>.editor` packages, using the new `libgodot_import_project`
  entry point.

### Properties

| Property | Default | Description |
|----------|---------|-------------|
| `TwoDogAutoImport` | `true` | Set `false` to disable the automatic import. |
| `TwoDogRequireImport` | `false` | Set `true` to fail the build when no import capability is available (instead of a warning). |
| `TwoDogForceImport` | `false` | Set `true` to force the import to run even when up-to-date. Also covers staleness the file tracking cannot see (deleted assets). |
| `TwoDogImportStampFile` | `<project>/.godot/2dog.import.stamp` | Override the stamp file location. |
| `TwoDogImportExcludes` |  –  | Extra semicolon-separated glob excludes for the import inputs (directories with a `.gdignore` are always excluded). |
| `GodotEditor` |  –  | Path to an external Godot editor binary. When set (or the `GODOT_EDITOR` environment variable is set), it is used instead of the in-process helper. |

When neither the helper payload nor an external editor can be resolved, the
build emits a warning and skips the import  –  mirroring the nonfatal runtime
behavior. Deleting the Godot project's `.godot/` directory forces a full
reimport on the next build.

## The import helper (`twodog.import`)

The `2dog.engine` package ships a small helper (`tools/net10.0/2dog.import.dll`) that
the MSBuild target invokes. It can also be run manually:

```bash
# In-process import via an editor-variant libgodot (no editor install needed)
dotnet run --project twodog.import -- \
    --libgodot godot/bin/godot.windows.editor.x86_64.shared_library.dll \
    --api-dir godot/bin/GodotSharp/Api/Debug \
    --tools-dir godot/bin/GodotSharp/Tools \
    ./demo

# Subprocess mode with an external Godot editor binary
dotnet run --project twodog.import -- --editor <godot-binary> ./demo
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<project-path>` | Path to a directory containing `project.godot` |
| `--libgodot <path>` | Editor-variant libgodot shared library; runs the import in-process via `libgodot_import_project` |
| `--api-dir <dir>` | Directory containing `GodotPlugins.dll` (sets `GODOTSHARP_DIR`); defaults to the helper's own directory |
| `--tools-dir <dir>` | Directory containing `GodotTools.dll` (sets `GODOT_TOOLS_DIR`); required in `--libgodot` mode |
| `--editor <path>` | External Godot editor binary (subprocess mode); falls back to the `GODOT_EDITOR` environment variable and takes precedence over `--libgodot` |
| `--verbose` | Pass `--verbose` to the engine |

The helper serializes concurrent imports of the same project with a lock file
(`.godot/2dog.import.lock`), so parallel builds of multiple consumers (e.g.
demo and tests) do not race.

::: info
Template builds (`template_debug`, `template_release`) cannot import; the
import pipeline requires the editor variant (`TOOLS_ENABLED`). The helper
reports this explicitly if pointed at a template libgodot.
:::

## `libgodot_import_project`

The Godot fork exposes a dedicated C entry point in editor builds of libgodot:

```c
LIBGODOT_API int libgodot_import_project(const char *p_project_path,
        int p_extra_argc, const char *p_extra_argv[]);
```

It runs the complete `godot --headless --import --path <project>` lifecycle
(setup → start → iterate until the first filesystem scan completes → cleanup)
in the calling process and returns a process-style exit code (`-1` when the
build has no editor). It must not be called while an embedded Godot instance
exists in the process; 2dog always calls it from a fresh helper process.

## Source builds and CI

With the automatic MSBuild import, a plain `dotnet build` of a consuming
project performs the import as part of the build, so CI pipelines do not
need a dedicated import step. In this repository, a source build
(`uv run poe build-godot` + `uv run poe build`) is enough: the first
`dotnet build` of the demo hosts or the tests imports the demo Godot project
against the locally built editor libgodot from `godot/bin/`.

To use an external Godot editor binary instead (for example one already
installed on a CI runner), set the `GodotEditor` MSBuild property or the
`GODOT_EDITOR` environment variable  –  the import target then shells out to
it rather than using the in-process helper.

## Troubleshooting

### Warning: no import capability was found

The `2dog.<rid>.editor` package (editor libgodot), the helper payload, or the
`2dog.tools` package (GodotTools assemblies) could not be resolved. Restore
the `2dog.engine` package normally (all of these are dependencies of it), or set
`<GodotEditor>` to an external editor binary.

### GodotTools.dll not found

The in-process import requires the GodotTools assemblies (the editor's C#
integration hard-requires them). They ship in the `2dog.tools` package; pass
`--tools-dir` when invoking the helper manually.

### No .uid files generated

Ensure the import actually ran (check for the `TwoDog: Imported Godot project`
build message) and that an *editor* build of libgodot or the editor binary was
used. Template builds do not support importing.
