using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Godot;
using Godot.NativeInterop;

namespace twodog;

/// <summary>
/// Browser (wasm) support for the GDExtension host. On web the engine is
/// statically linked into the .NET main module (dotnet.native.wasm), so the
/// libgodot entry points resolve via DirectPInvoke instead of
/// NativeLibrary.Load, and emscripten owns the main loop - there is no
/// blocking iteration loop, the engine calls back once per frame.
/// Web supports a single engine lifetime per page (no restart).
/// </summary>
[SupportedOSPlatform("browser")]
internal static unsafe partial class WebHost
{
    private static Action? _perFrame;
    private static Action? _onShutdown;
    private static bool _shutdownComplete;

    /// <summary>True while emscripten owns the main loop; Engine.Dispose() is a no-op then.</summary>
    internal static bool MainLoopActive { get; private set; }

    // Exported by platform/web/libgodot_web.cpp (statically linked).
    [LibraryImport("libgodot")]
    private static partial nint libgodot_create_godot_instance(int argc, nint* argv, nint initFunc);

    [LibraryImport("libgodot")]
    private static partial void libgodot_destroy_godot_instance(nint instance);

    // Runs one engine frame (redraw/max_fps handling + main_loop_iterate),
    // returns non-zero on quit.
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

    internal static nint CreateGodotInstance(int argc, nint* argv, nint initFunc) =>
        libgodot_create_godot_instance(argc, argv, initFunc);

    internal static void DestroyGodotInstance(nint instance) =>
        libgodot_destroy_godot_instance(instance);

    /// <summary>
    /// Hands the main loop to emscripten and returns immediately. The engine
    /// keeps running via per-frame callbacks; when Godot requests quit, the
    /// async teardown runs and <paramref name="onShutdown"/> destroys the
    /// instance.
    /// </summary>
    internal static void RunMainLoop(Action? perFrame, Action onShutdown)
    {
        if (MainLoopActive)
            throw new InvalidOperationException($"{nameof(WebHost)}: main loop is already running.");
        _perFrame = perFrame;
        _onShutdown = onShutdown;
        MainLoopActive = true;

        emscripten_set_main_loop((nint)(delegate* unmanaged<void>)&MainLoopCallback, -1, 0);

        // Run the first frame immediately (mirrors 2dog.engine's WebHost);
        // the engine may already request quit here.
        RunFrame();
    }

    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MainLoopCallback()
    {
        RunFrame();
    }

    /// <summary>
    /// One frame with the same semantics as the desktop Iteration() wrapper:
    /// iterate the engine, pump await continuations, drain deferred native
    /// releases at this known-safe point, then invoke the per-frame callback
    /// (skipped on the final iteration that requests quit).
    /// </summary>
    private static void RunFrame()
    {
        if (libgodot_web_iteration() != 0)
        {
            SetupExit();
            return;
        }

        GodotSynchronizationContext.PumpAll();
        DisposalQueue.Drain();

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
        // Swap the frame callback for the exit poller, then start Godot's
        // async teardown (IDBFS flush etc.). Once it signals completion the
        // poller destroys the instance and exits the runtime.
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
        try
        {
            _onShutdown?.Invoke();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }
        emscripten_cancel_main_loop();
        emscripten_force_exit(0);
    }
}
