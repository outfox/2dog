#if LIBGODOT_ENABLED
using System;

/// <summary>
/// Web (browser-wasm) bootstrap for 2dog hosts. Exposes the source-generated
/// <c>GodotPlugins.Game.Main.GetInitializePointer()</c> (internal to this
/// assembly) so the host can hand it to
/// <c>twodog.Engine.RegisterWebPluginsInitializer()</c>. Scripts are looked
/// up in the assembly that contains the generated initializer, which is why
/// this lives in the Godot project rather than the host.
/// </summary>
public static class TwoDogWebBoot
{
    public static IntPtr PluginsInitializer() => GodotPlugins.Game.Main.GetInitializePointer();
}
#endif
