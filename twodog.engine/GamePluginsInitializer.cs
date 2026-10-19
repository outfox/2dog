using System;
using System.Runtime.InteropServices;
using Godot.Bridge;
using Godot.NativeInterop;

namespace twodog;

/// <summary>
/// Registers the game assembly's generated plugins initializer (<c>TwoDogWebBoot.PluginsInitializer()</c>) with a
/// statically linked libgodot: GDMono then initializes through that pointer instead of loading GodotPlugins.dll.
/// Shared by the browser and iOS hosts.
/// </summary>
internal static unsafe partial class GamePluginsInitializer
{
    private static nint _pluginsInitializer;
    private static nint _initializePluginsCallback;
    private static bool _pluginsInitialized;

    // Exported by the mono module (GD_MONO_LIBGODOT_ENABLED); GDMono init calls the registered callback for the
    // godot_plugins_initialize pointer instead of loading GodotPlugins.dll.
    [LibraryImport("libgodot")]
    private static partial void set_load_from_executable_fn(nint callback);

    internal static bool HasPluginsInitializer => _pluginsInitializer != 0;

    /// <summary>
    /// Stores the game's godot_plugins_initialize pointer (<c>GodotPlugins.Game.Main.InitializeFromGameProject</c>)
    /// and registers it with libgodot. Must happen before the engine starts.
    /// </summary>
    internal static void Register(nint initializer)
    {
        if (initializer == 0)
            throw new ArgumentException($"{nameof(GamePluginsInitializer)}: plugins initializer pointer must not be null.", nameof(initializer));
        _pluginsInitializer = initializer;
        // Mono wasm AOT can abort in ldftn when a native callback takes another
        // UnmanagedCallersOnly method's address. Resolve it before native entry.
        _initializePluginsCallback = (nint)(delegate* unmanaged<nint, nint, nint, int, godot_bool>)&InitializePlugins;
        set_load_from_executable_fn((nint)(delegate* unmanaged<nint>)&LoadFromExecutable);
    }

    [UnmanagedCallersOnly]
    private static nint LoadFromExecutable() => _initializePluginsCallback;

    [UnmanagedCallersOnly]
    private static godot_bool InitializePlugins(nint godotDllHandle, nint outManagedCallbacks,
        nint unmanagedCallbacks, int unmanagedCallbacksSize)
    {
        try
        {
            var initialize = (delegate* unmanaged<nint, nint, nint, int, godot_bool>)_pluginsInitializer;
            var ok = initialize(godotDllHandle, outManagedCallbacks, unmanagedCallbacks, unmanagedCallbacksSize);
            if (!ok.ToBool()) return ok;
            // The game may compile against stock GodotSharp and run against the fork.
            // Keep the fork-only reset here, after the initializer refreshes native callbacks,
            // matching GodotPlugins on desktop without duplicating the generated initializer.
            if (_pluginsInitialized) ScriptManagerBridge.ResetForEngineReinitialization();
            _pluginsInitialized = true;
            return godot_bool.True;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return godot_bool.False;
        }
    }
}
