---
title: Resource Import
description: "How 2dog imports Godot resources and exports game packs during MSBuild: incremental imports driven by GodotProjectDir, the twodog.import helper, pack contents, and troubleshooting."
tagline: "Learn how 2dog imports your Godot resources while you build."
---

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
| `GodotEditor` | - | Path to an external Godot editor binary for imports and [pack exports](#pack-export). Only an explicit build setting selects this instead of the in-process helper; ambient `GODOT_EDITOR` and `GODOT4` variables do not affect imports or exports. |

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

## Pack export

Publishing a host also exports the game content through the host's export
preset, using the same helper. The pack holds resources only. The host's own
publish ships the code: the game assembly, the bindings, libgodot and the .NET
runtime. Godot's own .NET export step, which runs `dotnet publish` on the game
project, is skipped.

- Desktop packs sit beside the host executable, and the export copies the
  preset's GDExtension libraries next to them, where the engine loads them.
- Web packs embed their GDExtension side modules.
- A stock Godot editor set as `GodotEditor` exports a pack the standard way: it
  runs Godot's .NET publish and leaves GDExtension libraries out. Use the
  bundled helper when the game uses GDExtensions.

A stock Godot editor can still export the game the standard way. Its
`ExportDebug` and `ExportRelease` builds compile against the stock GodotSharp
bindings, because stock export templates need them, so code that uses 2dog-only
engine APIs does not compile there.

Packs exported later, such as patches, DLC or mods loaded with
`ProjectSettings.LoadResourcePack`, are resource-only as well. C# changes ship
with a host update, and a pack can only use script classes that the installed
host already contains.

## Troubleshooting

### Warning: no import capability was found

The editor libgodot (`2dog.<rid>.editor`), helper, or GodotTools assemblies
(`2dog.tools`) could not be resolved. Restore `2dog.engine`, which depends on
all three, or set `<GodotEditor>` to an external editor binary.

### GodotTools.dll not found

The bundled import needs the GodotTools assemblies from `2dog.tools`. Restore
`2dog.engine` normally, or configure an external editor explicitly with `<GodotEditor>`.

### No .uid files generated

Check for the `TwoDog: Imported Godot project` build message and confirm an
*editor* libgodot or editor binary was used. Template builds cannot import.
