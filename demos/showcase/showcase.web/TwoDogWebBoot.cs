#if LIBGODOT_ENABLED
using System;

namespace showcase.web;

/// <summary>
/// Web (browser-wasm) bootstrap for 2dog hosts. Exposes the game assembly's
/// plugins-initializer function pointer so the host can hand it to
/// <c>twodog.Engine.RegisterWebPluginsInitializer()</c>. Calls the fork SDK's
/// generated <c>GodotPlugins.Game.Main.GetInitializePointer()</c> directly;
/// scripts are looked up in the assembly that contains the generated
/// initializer, which is why this lives in the game project rather than the
/// host.
/// </summary>
public static class TwoDogWebBoot
{
    public static IntPtr PluginsInitializer() => GodotPlugins.Game.Main.GetInitializePointer();
}
#endif
