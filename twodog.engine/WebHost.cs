using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace twodog;

/// <summary>
/// Browser (wasm) support: libgodot is statically linked into dotnet.native.wasm, so the GodotPlugins initializer
/// is registered in-process and emscripten owns the main loop, calling back once per frame.
/// </summary>
[SupportedOSPlatform("browser")]
internal static unsafe partial class WebHost
{
    private static Func<bool>? _iterate;
    private static Action? _perFrame;
    private static Action? _onShutdown;
    private static bool _shutdownComplete;
    private static bool _shutdownStarted;

    /// <summary>True while emscripten owns the frame loop or asynchronous shutdown.</summary>
    internal static bool MainLoopActive { get; private set; }

    /// <summary>
    /// Whether quitting Godot also exits the wasm runtime (the standalone web host). Hosts sharing the runtime
    /// with other managed code (Blazor) keep it alive; the instance is still destroyed.
    /// </summary>
    internal static bool ExitRuntimeOnQuit { get; set; }

    // Exported by platform/web/libgodot_web.cpp: runs one engine frame
    // (redraw/max_fps handling + main_loop_iterate), returns non-zero on quit.
    [LibraryImport("libgodot")]
    private static partial byte libgodot_web_iteration();

    internal static bool Iteration() => libgodot_web_iteration() != 0;

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

    /// <summary>Registers the game's plugins initializer with the statically linked engine; see <see cref="GamePluginsInitializer"/>.</summary>
    internal static void RegisterPluginsInitializer(nint initializer) => GamePluginsInitializer.Register(initializer);

    internal static bool HasPluginsInitializer => GamePluginsInitializer.HasPluginsInitializer;

    /// <summary>
    /// Hands the main loop to emscripten and returns immediately; on quit the async teardown runs and
    /// <paramref name="onShutdown"/> destroys the instance.
    /// </summary>
    internal static void RunMainLoop(Func<bool> iterate, Action? perFrame, Action onShutdown)
    {
        if (MainLoopActive)
            throw new InvalidOperationException($"{nameof(WebHost)}: main loop is already running.");
        _iterate = iterate;
        _perFrame = perFrame;
        _onShutdown = onShutdown;
        MainLoopActive = true;

        try
        {
            emscripten_set_main_loop((nint)(delegate* unmanaged<void>)&MainLoopCallback, -1, 0);

            // Run the first frame immediately (mirrors the reference host in
            // Godot's LibGodotMain.cs); the engine may already request quit here.
            RunFrame();
        }
        catch
        {
            // Iteration can request disposal before throwing. Keep its exit poller alive.
            if (!_shutdownStarted) ResetMainLoop();
            throw;
        }
    }

    /// <summary>Stops the frame loop, if present, and begins the shared asynchronous teardown.</summary>
    internal static void BeginShutdown(Action onShutdown)
    {
        if (_shutdownStarted) return;
        _onShutdown = onShutdown;
        MainLoopActive = true;
        try
        {
            SetupExit();
        }
        catch
        {
            ResetMainLoop();
            throw;
        }
    }

    internal static void ResetMainLoop()
    {
        if (MainLoopActive) emscripten_cancel_main_loop();
        MainLoopActive = false;
        _shutdownComplete = false;
        _shutdownStarted = false;
        _iterate = null;
        _perFrame = null;
        _onShutdown = null;
    }

    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MainLoopCallback()
    {
        try
        {
            RunFrame();
        }
        catch (Exception e)
        {
            // Never unwind managed exceptions through Emscripten's native frame callback.
            Console.Error.WriteLine(e);
            SetupExit();
        }
    }

    /// <summary>
    /// One frame with desktop Run() semantics: iterate, then invoke the per-frame callback, skipping it on the
    /// final iteration that requests quit.
    /// </summary>
    private static void RunFrame()
    {
        if (_iterate!())
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
        if (_shutdownStarted) return;
        _shutdownStarted = true;
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

        var onShutdown = _onShutdown;
        var exitRuntime = ExitRuntimeOnQuit;
        emscripten_cancel_main_loop();
        MainLoopActive = false;
        _shutdownComplete = false;
        _shutdownStarted = false;
        _iterate = null;
        _perFrame = null;
        _onShutdown = null;
        try
        {
            onShutdown?.Invoke();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }
        if (exitRuntime)
            emscripten_force_exit(0);
    }
}
