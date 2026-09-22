#if TWODOG_WEB_BOOT
using System;
using Godot.NativeInterop;

namespace GodotPlugins.Game
{
    /// <summary>
    /// Extends the source-generated <c>GodotPlugins.Game.Main</c> to reach its private plugins initializer.
    /// Works with the stock Godot.NET.Sdk generators, no patched SDK required.
    /// </summary>
    internal static partial class Main
    {
        internal static unsafe IntPtr TwoDogGetInitializePointer() =>
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int, godot_bool>)&InitializeFromGameProject;
    }
}

/// <summary>
/// Public entry point for the web host assemblies. It cannot live in <c>Main</c>: the generated part declares
/// that class internal and partial parts may not disagree on accessibility. Compiled into the Godot project's
/// assembly (Compile Include in the root csproj) because scripts resolve from the assembly holding the initializer.
/// </summary>
public static class TwoDogWebBoot
{
    public static IntPtr PluginsInitializer() => GodotPlugins.Game.Main.TwoDogGetInitializePointer();
}
#endif
