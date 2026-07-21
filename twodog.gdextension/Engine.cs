using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using Godot.NativeInterop;

namespace twodog;

/// <summary>
/// Starts and pumps Godot as an embedded library over the GDExtension-based
/// bindings (no mono module, no GodotSharp). API mirrors 2dog.engine's Engine
/// so host code survives swapping the package reference:
/// <code>
/// using var engine = new Engine("myapp", "./project");
/// using var godot = engine.Start();
/// while (!godot.Iteration()) { /* per frame */ }
/// </code>
/// </summary>
public sealed unsafe class Engine : IDisposable
{
    private static nint _godotInstancePtr;
    private static delegate* unmanaged<nint, void> _destroyGodotInstance;

    private readonly string _project;
    private readonly string _path;
    private readonly string[] _args;
    private GodotInstance? _instance;
    private bool _ownsInstance;

    public Engine(string project, string path = ".", params string[] args)
    {
        _project = project;
        _path = path;
        _args = args;
    }

    /// <summary>
    /// Native variant to load: 'release', 'debug', or 'editor'. Defaults to the
    /// consuming assembly's TwoDogVariant metadata (stamped by the package
    /// targets), else 'debug'.
    /// </summary>
    public string Variant { get; init; } = DetectVariant();

    public SceneTree Tree => Godot.Engine.GetMainLoop() as SceneTree ??
                             throw new NullReferenceException($"{nameof(Engine)}: Failed to get SceneTree.");

    /// <summary>Full path of the loaded libgodot native (null before Start).</summary>
    public static string? LoadedNativePath => NativeLoader.LoadedLibraryPath;

    /// <summary>
    /// Explicit libgodot path. When set, it is loaded as-is (no variant
    /// resolution, no fallback) and <see cref="Variant"/> is ignored.
    /// </summary>
    public string? NativePath { get; init; }

    public GodotInstance Start()
    {
        if (_godotInstancePtr != 0)
            throw new InvalidOperationException(
                $"{nameof(Engine)}: A Godot instance is already running. Only one instance may exist at a time " +
                "(a Godot limitation) - dispose the previous Engine before starting a new one.");

        var lib = NativePath is { } exact ? NativeLoader.LoadExact(exact) : NativeLoader.Load(Variant);
        var create = (delegate* unmanaged<int, nint*, nint, nint>)NativeLibrary.GetExport(lib, "libgodot_create_godot_instance");
        _destroyGodotInstance = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(lib, "libgodot_destroy_godot_instance");
        RegisterProcessExitSweep();

        List<string> godotArgs = [_project, "--path", _path, .. _args];
        var argv = new nint[godotArgs.Count];
        for (var i = 0; i < godotArgs.Count; i++) argv[i] = Marshal.StringToCoTaskMemUTF8(godotArgs[i]);
        try
        {
            fixed (nint* argvPtr = argv)
            {
                _godotInstancePtr = create(godotArgs.Count, argvPtr, GdExtensionHost.InitCallbackPointer);
            }
        }
        finally
        {
            foreach (var p in argv) Marshal.FreeCoTaskMem(p);
        }

        if (_godotInstancePtr == 0)
            throw new InvalidOperationException($"{nameof(Engine)}: Error creating Godot instance.");
        if (!GdExtensionHost.Loaded)
            throw new InvalidOperationException(
                $"{nameof(Engine)}: Missing GDExtension procs: {GdExtensionHost.MissingProcsDisplay}");

        var native = (Godot.GodotInstance?)InstanceBindings.GetOrCreate(_godotInstancePtr, adoptRef: false)
                     ?? throw new InvalidOperationException($"{nameof(Engine)}: Failed to wrap GodotInstance.");

        if (!native.Start())
        {
            Destroy();
            throw new InvalidOperationException($"{nameof(Engine)}: Error starting Godot instance.");
        }

        // Async support: await Task.* continuations marshal back to this
        // thread, pumped once per Iteration.
        GodotSynchronizationContext.Install();

        _ownsInstance = true;
        _instance = new GodotInstance(native);
        return _instance;
    }

    /// <summary>Runs the main loop to quit, invoking <paramref name="perFrame"/> every iteration.</summary>
    public void Run(Action? perFrame = null)
    {
        if (_instance is null) throw new InvalidOperationException($"{nameof(Engine)}: Call Start() first.");
        while (!_instance.Iteration())
        {
            perFrame?.Invoke();
        }
    }

    public void Dispose()
    {
        if (!_ownsInstance || _godotInstancePtr == 0) return;
        _ownsInstance = false;
        Destroy();
    }

    private static void Destroy()
    {
        DisposalQueue.Drain();
        _destroyGodotInstance(_godotInstancePtr);
        _godotInstancePtr = 0;
    }

    private static string DetectVariant()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                {
                    if (attr.Key == "TwoDogVariant" && !string.IsNullOrEmpty(attr.Value))
                        return attr.Value;
                }
            }
            catch
            {
                // Dynamic assemblies may throw on attribute reflection.
            }
        }
        return "debug";
    }

    private static bool _sweepRegistered;

    /// <summary>
    /// Unload libgodot before loader shutdown: leaving it mapped crashes the
    /// process on Windows with 0xE0464645 (same mitigation as 2dog.engine).
    /// No Godot API may be called from this handler.
    /// </summary>
    private static void RegisterProcessExitSweep()
    {
        if (_sweepRegistered) return;
        _sweepRegistered = true;
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            if (!OperatingSystem.IsWindows() || NativeLoader.LoadedLibraryPath is not { } path) return;
            var name = Path.GetFileName(path);
            var module = GetModuleHandleW(name);
            var attempts = 0;
            while (module != 0 && FreeLibrary(module) && ++attempts < 32)
            {
                module = GetModuleHandleW(name);
            }
        };
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string moduleName);

    [DllImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}

/// <summary>
/// The running Godot instance: pump it with <see cref="Iteration"/> (returns
/// true when the engine wants to quit). Each iteration drains the deferred
/// native-release queue at a known-safe point - the reason to pump through
/// this wrapper rather than the raw engine object.
/// </summary>
public sealed class GodotInstance(Godot.GodotInstance native) : IDisposable
{
    /// <summary>The underlying engine object (fork class, typed via the generated API).</summary>
    public Godot.GodotInstance Native { get; } = native;

    /// <summary>Advances the engine one frame. Returns true when quitting.</summary>
    public bool Iteration()
    {
        var quit = Native.Iteration();
        GodotSynchronizationContext.PumpAll();
        DisposalQueue.Drain();
        return quit;
    }

    public bool IsStarted => Native.IsStarted();

    public void Dispose()
    {
        // Instance lifetime is owned by Engine (libgodot_destroy_godot_instance);
        // Dispose exists for using-pattern parity with 2dog.engine.
    }
}
