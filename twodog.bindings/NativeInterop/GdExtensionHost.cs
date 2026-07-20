using System.Runtime.InteropServices;

namespace Godot.NativeInterop;

/// <summary>
/// GDExtension entry-point plumbing for an embedding host. The host passes
/// <see cref="InitCallback"/> to libgodot_create_godot_instance; everything
/// downstream of get_proc_address is owned here.
///
/// Lifecycle note (verified by the phase-0 spike): libgodot runs CORE/SERVERS
/// initialization during instance creation, but SCENE and EDITOR levels run
/// during GodotInstance.start(). Anything touching scene classes must hook
/// <see cref="LevelInitialized"/> and wait for the Scene level.
/// </summary>
public static unsafe class GdExtensionHost
{
    /// <summary>The extension library token Godot assigned us (class_userdata for registration).</summary>
    public static nint Library { get; private set; }

    public static bool Loaded { get; private set; }

    public static string MissingProcsDisplay =>
        GdExtensionInterface.MissingProcs.Count == 0 ? "none" : string.Join(", ", GdExtensionInterface.MissingProcs);

    public static event Action<GDExtensionInitializationLevel>? LevelInitialized;
    public static event Action<GDExtensionInitializationLevel>? LevelDeinitialized;

    /// <summary>Function-pointer form of <see cref="InitCallback"/> for passing to libgodot.</summary>
    public static nint InitCallbackPointer =>
        (nint)(delegate* unmanaged<nint, nint, GDExtensionInitialization*, byte>)&InitCallback;

    [UnmanagedCallersOnly]
    public static byte InitCallback(nint getProcAddress, nint library, GDExtensionInitialization* init)
    {
        GdExtensionInterface.Load(getProcAddress);
        Library = library;
        Loaded = GdExtensionInterface.MissingProcs.Count == 0;
        if (!Loaded)
        {
            Console.Error.WriteLine(
                "twodog.bindings: missing GDExtension procs: " +
                string.Join(", ", GdExtensionInterface.MissingProcs));
        }

        init->minimum_initialization_level = GDExtensionInitializationLevel.GDEXTENSION_INITIALIZATION_CORE;
        init->initialize = (nint)(delegate* unmanaged<nint, int, void>)&OnInitialize;
        init->deinitialize = (nint)(delegate* unmanaged<nint, int, void>)&OnDeinitialize;
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnInitialize(nint userdata, int level) =>
        LevelInitialized?.Invoke((GDExtensionInitializationLevel)level);

    [UnmanagedCallersOnly]
    private static void OnDeinitialize(nint userdata, int level) =>
        LevelDeinitialized?.Invoke((GDExtensionInitializationLevel)level);
}
