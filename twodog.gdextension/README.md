# 2dog.gdextension

Host layer for the GDExtension-based 2dog stack: start and pump Godot's
MainLoop from your C# application - no mono module, no GodotSharp. Drop-in
alternative to [2dog.engine](https://www.nuget.org/packages/2dog.engine/):
swap the package reference, keep your code.

Ships the C# bindings via [2dog.bindings](https://www.nuget.org/packages/2dog.bindings/)
and the `[Export]`/`[Signal]` source generator as an analyzer.

Pair with a natives package for your platform:

- `2dog.gdextension.win-x64`
- `2dog.gdextension.linux-x64`
- `2dog.gdextension.osx-arm64`

Variant selection follows `$(TwoDogVariant)` (`release` default, `debug`, `editor`).
