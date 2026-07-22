# 2dog.gdextension

Host layer for the GDExtension-based 2dog stack: start and pump Godot's
MainLoop from your C# application - no mono module, no GodotSharp. Drop-in
alternative to [2dog.engine](https://www.nuget.org/packages/2dog.engine/):
swap the package reference, keep your code.

Ships the C# bindings via [2dog.bindings](https://www.nuget.org/packages/2dog.bindings/)
and the `[Export]`/`[Signal]` source generator as an analyzer. Classes deriving
`Godot.GodotObject` register with the engine automatically on assembly load
(opt out per class with `[SkipAutoRegister]`; base-before-derived ordering and
the pre-engine queue are handled for you).

Scenes work like GodotSharp: `.tscn` files referencing `res://*.cs` scripts
attach your managed classes at load (a built-in GDExtension script language
resolves the file-stem class name against the registered classes). Exported
members keep their verbatim C# names, scene-saved values apply, engine
virtuals (`_Ready`, `_Process`, ...) dispatch, and GDScript neighbors read
properties, call methods, and connect to `[Signal]` signals on your C# nodes.

Pair with a natives package for your platform:

- `2dog.gdextension.win-x64`
- `2dog.gdextension.linux-x64`
- `2dog.gdextension.osx-arm64`

Variant selection follows `$(TwoDogVariant)` (`release` default, `debug`, `editor`).
