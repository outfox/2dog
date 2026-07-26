# Resource Import

Godot must import resources before a project can run. This generates C# script
`.uid` files, processes assets, and builds the `.godot/` cache. Without it, the
engine reports missing UIDs. 2dog handles the import **automatically during
build**.

## Automatic import (MSBuild)

Any project that sets `<GodotProjectDir>` and references `2dog.engine` gets an
incremental import after `Build`:

- Changed project files trigger an import. Otherwise, a shared stamp in
  `<project>/.godot/` marks the project up to date.
- `.gdignore` directories are not tracked, so editing a nested 2dog host does
  not trigger an import. Use `TwoDogImportExcludes` for more exclusions.
- No installed Godot editor is required. A helper process uses the
  editor-variant libgodot from `2dog.<rid>.editor`.

### Properties

| Property | Default | Description |
|----------|---------|-------------|
| `TwoDogAutoImport` | `true` | Set `false` to disable the automatic import. |
| `TwoDogRequireImport` | `false` | Set `true` to fail the build when no import capability is available (instead of a warning). |
| `TwoDogForceImport` | `false` | Set `true` to force the import to run even when up-to-date. Also covers staleness the file tracking cannot see (deleted assets). |
| `TwoDogImportStampFile` | `<project>/.godot/2dog.import.stamp` | Override the stamp file location. |
| `TwoDogImportExcludes` | - | Extra semicolon-separated glob excludes for the import inputs (directories with a `.gdignore` are always excluded). |
| `GodotEditor` | - | Path to an external Godot editor binary. When set (or the `GODOT_EDITOR` environment variable is set), it is used instead of the in-process helper. |

When neither the helper payload nor an external editor can be resolved, the
build warns and skips the import. Set `TwoDogRequireImport` to fail instead.
Deleting `.godot/` forces a full import on the next build.

## The import helper (`twodog.import`)

`2dog.engine` includes `tools/net10.0/2dog.import.dll`, which MSBuild runs in a
separate process. A `.godot/2dog.import.lock` serializes concurrent imports of
the same project.

::: info
Template builds (`template_debug`, `template_release`) cannot import; the
pipeline requires the editor variant (`TOOLS_ENABLED`).
:::

## Troubleshooting

### Warning: no import capability was found

The editor libgodot (`2dog.<rid>.editor`), helper, or GodotTools assemblies
(`2dog.tools`) could not be resolved. Restore `2dog.engine`, which depends on
all three, or set `<GodotEditor>` to an external editor binary.

### GodotTools.dll not found

The bundled import needs the GodotTools assemblies from `2dog.tools`. Restore
`2dog.engine` normally, or configure an external editor with `<GodotEditor>` or
`GODOT_EDITOR`.

### No .uid files generated

Check for the `TwoDog: Imported Godot project` build message and confirm an
*editor* libgodot or editor binary was used. Template builds cannot import.
