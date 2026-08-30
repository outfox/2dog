using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Godot.Bridge;
using Godot.NativeInterop;

namespace twodog;

/// <summary>
/// Browser (wasm) support: libgodot is statically linked into dotnet.native.wasm, so the GodotPlugins initializer
/// is registered in-process and emscripten owns the main loop, calling back once per frame.
/// </summary>
[SupportedOSPlatform("browser")]
internal static unsafe partial class WebHost
{
    private static nint _pluginsInitializer;
    private static int _bootCount;
    private static Action? _perFrame;
    private static Action? _onShutdown;
    private static bool _shutdownComplete;

    /// <summary>True while emscripten owns the main loop; Engine.Dispose() is a no-op then.</summary>
    internal static bool MainLoopActive { get; private set; }

    /// <summary>
    /// Whether quitting Godot also exits the wasm runtime (the standalone web host). Hosts sharing the runtime
    /// with other managed code (Blazor) keep it alive; the instance is still destroyed.
    /// </summary>
    internal static bool ExitRuntimeOnQuit { get; set; } = true;

    // Exported by the mono module (GD_MONO_LIBGODOT_ENABLED); GDMono init calls the registered callback for the
    // godot_plugins_initialize pointer instead of loading GodotPlugins.dll.
    [LibraryImport("libgodot")]
    private static partial void set_load_from_executable_fn(nint callback);

    // Exported by platform/web/libgodot_web.cpp: runs one engine frame
    // (redraw/max_fps handling + main_loop_iterate), returns non-zero on quit.
    [LibraryImport("libgodot")]
    private static partial byte libgodot_web_iteration();

    // Emscripten runtime functions, resolved statically against the main
    // module by the wasm pinvoke table ("*" convention).
    [DllImport("*")]
    private static extern void emscripten_set_main_loop(nint func, int fps, byte simulateInfiniteLoop);

    [DllImport("*")]
    private static extern void emscripten_cancel_main_loop();

    [DllImport("*")]
    private static extern void emscripten_force_exit(int status);

    // Godot web JS glue: flushes IDBFS and other async teardown, then invokes
    // the callback.
    [DllImport("*")]
    private static extern void godot_js_os_finish_async(nint callback);

    [UnmanagedCallersOnly]
    private static nint LoadFromExecutable() =>
        (nint)(delegate* unmanaged<nint, nint, nint, int, godot_bool>)&InitializeFromGameProject;

    /// <summary>
    /// GDMono's plugins initializer for every engine lifetime. The game's source-generated initializer runs once
    /// (its DllImportResolver registration cannot repeat); later lifetimes redo what it does per engine - reset the
    /// script bridge (which replays the script lookups), refresh the native callbacks, export the managed ones.
    /// </summary>
    [UnmanagedCallersOnly]
    private static godot_bool InitializeFromGameProject(nint godotDllHandle, nint outManagedCallbacks,
        nint unmanagedCallbacks, int unmanagedCallbacksSize)
    {
        try
        {
            if (_bootCount == 0)
            {
                var initialize = (delegate* unmanaged<nint, nint, nint, int, godot_bool>)_pluginsInitializer;
                var ok = initialize(godotDllHandle, outManagedCallbacks, unmanagedCallbacks, unmanagedCallbacksSize);
                if (ok.ToBool())
                    _bootCount++;
                return ok;
            }

            ScriptManagerBridge.ResetForEngineReinitialization();
            NativeFuncs.Initialize(unmanagedCallbacks, unmanagedCallbacksSize);
            ManagedCallbacks.Create(outManagedCallbacks);
            _bootCount++;
            return godot_bool.True;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return godot_bool.False;
        }
    }

    /// <summary>
    /// Stores the game's godot_plugins_initialize pointer (<c>GodotPlugins.Game.Main.InitializeFromGameProject</c>)
    /// and registers it with libgodot. Must happen before the engine starts.
    /// </summary>
    internal static void RegisterPluginsInitializer(nint initializer)
    {
        if (initializer == 0)
            throw new ArgumentException($"{nameof(WebHost)}: plugins initializer pointer must not be null.", nameof(initializer));
        _pluginsInitializer = initializer;
        set_load_from_executable_fn((nint)(delegate* unmanaged<nint>)&LoadFromExecutable);
    }

    internal static bool HasPluginsInitializer => _pluginsInitializer != 0;

    /// <summary>
    /// Hands the main loop to emscripten and returns immediately; on quit the async teardown runs and
    /// <paramref name="onShutdown"/> destroys the instance.
    /// </summary>
    internal static void RunMainLoop(Action? perFrame, Action onShutdown)
    {
        if (MainLoopActive)
            throw new InvalidOperationException($"{nameof(WebHost)}: main loop is already running.");
        _perFrame = perFrame;
        _onShutdown = onShutdown;
        MainLoopActive = true;

        emscripten_set_main_loop((nint)(delegate* unmanaged<void>)&MainLoopCallback, -1, 0);

        // Run the first frame immediately (mirrors the reference host in
        // Godot's LibGodotMain.cs); the engine may already request quit here.
        RunFrame();
    }

    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MainLoopCallback()
    {
        RunFrame();
    }

    /// <summary>
    /// One frame with desktop Run() semantics: iterate, then invoke the per-frame callback, skipping it on the
    /// final iteration that requests quit.
    /// </summary>
    private static void RunFrame()
    {
        if (libgodot_web_iteration() != 0)
        {
            SetupExit();
            return;
        }

        var perFrame = _perFrame;
        if (perFrame != null)
        {
            try
            {
                perFrame();
            }
            catch (Exception e)
            {
                // Exceptions must not escape into the emscripten loop.
                Console.Error.WriteLine(e);
            }
        }
    }

    private static void SetupExit()
    {
        // Swap the frame callback for the exit poller and start Godot's async teardown (IDBFS flush etc.); the
        // poller destroys the instance once it completes.
        emscripten_cancel_main_loop();
        emscripten_set_main_loop((nint)(delegate* unmanaged<void>)&ExitCallback, -1, 0);
        godot_js_os_finish_async((nint)(delegate* unmanaged<void>)&CleanupAfterSync);
    }

    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CleanupAfterSync()
    {
        _shutdownComplete = true;
    }

    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ExitCallback()
    {
        if (!_shutdownComplete)
        {
            return; // Still waiting for async teardown.
        }

        MainLoopActive = false;
        _shutdownComplete = false;
        try
        {
            _onShutdown?.Invoke();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }
        emscripten_cancel_main_loop();
        if (ExitRuntimeOnQuit)
            emscripten_force_exit(0);
    }
}
