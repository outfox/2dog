---
title: Resource Import
description: "How 2dog imports Godot resources and exports game packs during MSBuild: incremental imports driven by GodotProjectDir, the twodog.import helper, pack contents, and troubleshooting."
tagline: "Learn how 2dog imports your Godot resources while you build."
---

# Resource Import

Godot must import resources before a project can run. The import generates
`.uid` files for C# scripts, processes assets, and builds the `.godot/` cache. Without it, the
engine reports missing UIDs. 2dog runs the import **automatically during
the build**.

## Automatic import (MSBuild)

Any project that sets `<GodotProjectDir>` and references `2dog.engine` gets an
incremental import after `Build`:

- Changed project files trigger an import. Otherwise, a shared stamp in
  `<project>/.godot/` marks the project up to date.
- The import does not track directories with a `.gdignore` file, so a change
  to a nested 2dog host does not trigger an import. Use `TwoDogImportExcludes` for more exclusions.
- No installed Godot editor is required. A helper process uses the
  editor-variant libgodot from `2dog.<rid>.editor`.

### Properties

| Property                | Default                              | Description                                                                                                                                                                                                                                                                |
|-------------------------|--------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `TwoDogAutoImport`      | `true`                               | Set `false` to disable the automatic import.                                                                                                                                                                                                                               |
| `TwoDogRequireImport`   | `false`                              | Set `true` to fail the build when no import capability is available (instead of a warning).                                                                                                                                                                                |
| `TwoDogForceImport`     | `false`                              | Set `true` to run the import even when the project is up to date. This also catches changes that the file tracking cannot see, such as deleted assets.                                                                                                                     |
| `TwoDogImportStampFile` | `<project>/.godot/2dog.import.stamp` | Override the stamp file location.                                                                                                                                                                                                                                          |
| `TwoDogImportExcludes`  | -                                    | Extra semicolon-separated glob excludes for the import inputs (directories with a `.gdignore` are always excluded).                                                                                                                                                        |
| `GodotEditor`           | -                                    | Path to an external Godot editor binary for imports and [pack exports](#pack-export). The build uses this editor instead of the in-process helper only when you set this property. The `GODOT_EDITOR` and `GODOT4` environment variables do not affect imports or exports. |

If the build cannot find the helper payload or an external editor, it shows a
warning and skips the import. Set `TwoDogRequireImport` to fail instead.
Deleting `.godot/` forces a full import on the next build.

## The import helper (`twodog.import`)

`2dog.engine` runs the import in a separate helper process. It runs only one
import per project at a time. The helper needs the `editor` variant, because
the `debug` and `release` natives cannot import.

## Pack export

When you publish a host, the same helper also exports the game content
through the host's export preset. The pack holds resources only. The host's own
publish ships the code: the game assembly, the bindings, libgodot and the .NET
runtime. 2dog skips Godot's own .NET export step, which runs `dotnet publish` on the
game project.

- Desktop packs sit beside the host executable, and the export copies the
  preset's GDExtension libraries next to them, where the engine loads them.
- Web packs embed their GDExtension side modules.
- A stock Godot editor, also when you set it as `GodotEditor`, exports the
  standard way. It runs Godot's .NET publish against the stock bindings, so
  2dog-only engine APIs do not compile there. It also does not include the
  GDExtension libraries.

Packs exported later, such as patches, DLC or mods loaded with
`ProjectSettings.LoadResourcePack`, are resource-only as well. C# changes ship
with a host update, and a pack can only use script classes that the installed
host already contains.

## Troubleshooting

### No import capability, or GodotTools.dll not found

The build could not find the editor libgodot (`2dog.<rid>.editor`), the
helper, or the GodotTools assemblies (`2dog.tools`). Restore `2dog.engine`, which depends on
all three, or set `<GodotEditor>` to an external editor binary.

### No .uid files generated

Look for the `TwoDog: Imported Godot project` build message. Make sure that
the build used an *editor* libgodot or an editor binary.
