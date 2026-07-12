# 2dog.tools

Godot editor tooling assemblies (GodotTools and dependencies) for
[2dog](https://github.com/outfox/2dog).

These platform-agnostic managed assemblies are required by 2dog's automatic
resource import, which runs the Godot editor's `--headless --import` pipeline
against the editor-variant libgodot shipped in the `2dog.<rid>.editor`
packages. The editor's C# initialization requires GodotTools to be present.

This package is referenced automatically by the main `2dog` package; you
should not need to reference it directly.
