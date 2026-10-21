using System.Runtime.InteropServices;
using System.Text;
using CoreFoundation;
using Foundation;
using Godot;
using ObjCRuntime;
using UIKit;

namespace twodog.Ios;

/// <summary>
/// The Godot engine of an iOS app that links libgodot statically. <see cref="Start"/> registers the .NET runtime,
/// creates the engine and returns Godot's view controller; its display link then runs every frame on the main
/// thread, so UIKit code and Godot share one thread. One engine per process.
/// </summary>
/// <remarks>Experimental. Never call <c>Engine.Start()</c> on iOS.</remarks>
public static unsafe class GodotHost
{
    [DllImport("__Internal")]
    private static extern int libgodot_ios_create(int argc, IntPtr argv, IntPtr initFunc);

    [DllImport("__Internal")]
    private static extern IntPtr libgodot_ios_view_controller();

    [DllImport("__Internal")]
    private static extern void libgodot_ios_set_callbacks(IntPtr started, IntPtr quitRequested, IntPtr userdata);

    [DllImport("__Internal")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool libgodot_ios_is_running();

    [DllImport("__Internal")]
    private static extern void libgodot_ios_request_quit();

    private static readonly object Sync = new();
    private static UIViewController? _viewController;
    // Godot keeps the executable path from argv[0]; the array stays allocated for the process.
    private static IntPtr _argv;

    /// <summary>Raised on the main thread once the main loop runs and the scene tree exists.</summary>
    public static event Action? Started;

    /// <summary>Raised once, on the main thread, when the main loop asks to quit; the view keeps drawing until it is removed.</summary>
    public static event Action? QuitRequested;

    /// <summary>Godot's view controller, null before <see cref="Start"/>.</summary>
    public static UIViewController? ViewController => _viewController;

    /// <summary>Whether the engine was created.</summary>
    public static bool IsCreated => _viewController is not null;

    /// <summary>Whether the main loop runs (after <see cref="Started"/>).</summary>
    public static bool IsRunning => IsCreated && libgodot_ios_is_running();

    /// <summary>
    /// Registers the game's plugins initializer, creates the engine with <c>--main-pack</c> pointing at the pack
    /// the 2dog.ios targets bundle (or <paramref name="packPath"/>) and returns Godot's view controller.
    /// </summary>
    /// <param name="project">The game assembly name; Godot sees it as its program name.</param>
    /// <param name="pluginsInitializer">
    /// <c>TwoDogWebBoot.PluginsInitializer()</c> from the game project, or zero when <see cref="IosHost.Register"/>
    /// already ran (a MAUI app registers in its delegate before the page creates the view).
    /// </param>
    /// <param name="packPath">Absolute path of the game pack; null uses the bundled <c>game.pck</c>.</param>
    /// <param name="args">Additional Godot command-line arguments.</param>
    public static UIViewController Start(string project, IntPtr pluginsInitializer = default, string? packPath = null, params string[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        lock (Sync)
        {
            if (_viewController is not null)
                throw new InvalidOperationException("The Godot engine was already started in this process.");

            if (pluginsInitializer != IntPtr.Zero) IosHost.Register(pluginsInitializer);
            else if (!IosHost.IsRegistered)
                throw new InvalidOperationException(
                    "Pass TwoDogWebBoot.PluginsInitializer() from the game project, or call IosHost.Register with it first.");

            packPath ??= NSBundle.MainBundle.PathForResource("game", "pck")
                ?? throw new FileNotFoundException("game.pck is not in the app bundle; the 2dog.ios targets add it during the build.");

            var arguments = new List<string> { project, "--main-pack", packPath };
            arguments.AddRange(args);
            _argv = Marshal.AllocHGlobal(IntPtr.Size * (arguments.Count + 1));
            for (var i = 0; i < arguments.Count; i++)
                Marshal.WriteIntPtr(_argv, i * IntPtr.Size, Utf8(arguments[i]));
            Marshal.WriteIntPtr(_argv, arguments.Count * IntPtr.Size, IntPtr.Zero);

            var error = libgodot_ios_create(arguments.Count, _argv, IntPtr.Zero);
            if (error != 0)
                throw new InvalidOperationException($"Godot failed to set up (error {error}); see the device log.");

            libgodot_ios_set_callbacks(
                (IntPtr)(delegate* unmanaged<IntPtr, void>)&OnStarted,
                (IntPtr)(delegate* unmanaged<IntPtr, void>)&OnQuitRequested,
                IntPtr.Zero);

            var handle = libgodot_ios_view_controller();
            _viewController = Runtime.GetNSObject<UIViewController>(handle)
                ?? throw new InvalidOperationException("Godot returned no view controller.");
            return _viewController;
        }
    }

    /// <summary>Runs the action on the main queue, between frames, with the scene tree; throws before <see cref="Started"/>.</summary>
    public static void Dispatch(Action<SceneTree> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsRunning) throw new InvalidOperationException("Godot has not started.");
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            if (Godot.Engine.GetMainLoop() is SceneTree tree) action(tree);
        });
    }

    /// <summary>Asks the main loop to quit, like closing a window.</summary>
    public static void RequestQuit()
    {
        if (IsCreated) libgodot_ios_request_quit();
    }

    [UnmanagedCallersOnly]
    private static void OnStarted(IntPtr userdata)
    {
        try
        {
            Started?.Invoke();
        }
        catch (Exception e)
        {
            // Must not unwind into the display-link frame.
            Console.Error.WriteLine($"{nameof(GodotHost)}: {nameof(Started)} handler failed: {e}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnQuitRequested(IntPtr userdata)
    {
        try
        {
            QuitRequested?.Invoke();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"{nameof(GodotHost)}: {nameof(QuitRequested)} handler failed: {e}");
        }
    }

    private static IntPtr Utf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var buffer = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        Marshal.WriteByte(buffer, bytes.Length, 0);
        return buffer;
    }
}
