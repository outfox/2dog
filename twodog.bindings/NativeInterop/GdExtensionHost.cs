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

    /// <summary>True once SCENE-level initialization ran (scene classes exist in ClassDB).</summary>
    public static bool SceneLevelInitialized { get; private set; }

    /// <summary>Function-pointer form of <see cref="InitCallback"/> for passing to libgodot.</summary>
    public static nint InitCallbackPointer =>
        (nint)(delegate* unmanaged<nint, nint, GDExtensionInitialization*, byte>)&InitCallback;

    [UnmanagedCallersOnly]
    public static byte InitCallback(nint getProcAddress, nint library, GDExtensionInitialization* init)
    {
        try
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
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog.bindings: unhandled exception in init callback: {e}");
            return 0;
        }
    }

    // LevelInitialized/LevelDeinitialized run subscriber (user) code - never
    // let their exceptions unwind into the engine's init machinery.
    [UnmanagedCallersOnly]
    private static void OnInitialize(nint userdata, int level)
    {
        try
        {
            if ((GDExtensionInitializationLevel)level == GDExtensionInitializationLevel.GDEXTENSION_INITIALIZATION_CORE)
            {
                // GodotInstance drives startup (Engine.Start -> GodotInstance.Start
                // runs the remaining init levels), so its binds must resolve at
                // CORE init - the only typed API available before SCENE init.
                GodotInstance.__ResolveBinds();
            }
            if ((GDExtensionInitializationLevel)level == GDExtensionInitializationLevel.GDEXTENSION_INITIALIZATION_SCENE)
            {
                // Resolve the typed API's method binds before any subscriber
                // (or queued class registration) can call into it. Re-runs on
                // every engine start, refreshing binds across restarts.
                GeneratedBinds.ResolveScene();
                SceneLevelInitialized = true;
            }
            if ((GDExtensionInitializationLevel)level == GDExtensionInitializationLevel.GDEXTENSION_INITIALIZATION_EDITOR)
                GeneratedBinds.ResolveEditor();
            LevelInitialized?.Invoke((GDExtensionInitializationLevel)level);
            // After user class registrations flushed: wire the C# script
            // language (scenes referencing res://*.cs scripts).
            if ((GDExtensionInitializationLevel)level == GDExtensionInitializationLevel.GDEXTENSION_INITIALIZATION_SCENE)
                ScriptShim.Initialize();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog.bindings: unhandled exception in level-initialized handler: {e}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnDeinitialize(nint userdata, int level)
    {
        try
        {
            if ((GDExtensionInitializationLevel)level == GDExtensionInitializationLevel.GDEXTENSION_INITIALIZATION_SCENE)
            {
                ScriptShim.Shutdown();
                // The scene tree and script instances are gone, so wrappers of
                // engine resources are unrooted garbage. Run their finalizer ->
                // DisposalQueue pipeline now - extension classes are still
                // registered at this point (they unregister after this callback),
                // and ObjectDB's exit leak accounting would otherwise count our
                // owned refs as leaks. Two rounds: a release can unroot more.
                for (var i = 0; i < 2; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    DisposalQueue.Drain();
                }
            }
            LevelDeinitialized?.Invoke((GDExtensionInitializationLevel)level);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog.bindings: unhandled exception in level-deinitialized handler: {e}");
        }
    }
}
