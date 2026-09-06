using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;

namespace twodog;

/// <summary>Configures and owns an embedded Godot instance.</summary>
public class Engine : IDisposable, IAsyncDisposable
{
    private enum LifecycleState
    {
        Created,
        Starting,
        Running,
        Stopping,
        Stopped,
        Disposed
    }

    private static readonly object ProcessSync = new();
    private static IntPtr _godotInstancePtr = IntPtr.Zero;
    private static bool _destroyingInstance;
    private readonly string _project;
    private readonly string[] _args;
    private readonly string? _projectPath;

    // Only the Engine that started the process-wide instance may destroy it: disposing one whose Start()
    // failed or never ran must not tear down another Engine's instance.
    private readonly object _sync = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LifecycleState _state;
    private IntPtr _ownedInstancePtr;
    private GodotInstance? _godotInstance;
    private bool _startedLifetime;
    private bool _disposeRequested;
    private bool _iterationActive;
    private bool _runActive;
    private bool _teardownStarted;
    private bool _exitNotified;
    private int _ownerThreadId;

    /// <summary>Creates an engine and resolves its content when no path is supplied.</summary>
    /// <param name="project">Label passed as Godot's first argument.</param>
    /// <param name="path">Project directory; null = desktop auto-resolves content, browser loads the web pack.</param>
    /// <param name="args">Additional arguments passed to Godot.</param>
    public Engine(string project, string? path = null, params string[] args)
    {
        _project = project;
        _args = args;

        if (path is null && !System.OperatingSystem.IsBrowser())
            path = ResolveContent();
        _projectPath = string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
    }

    // .NET's Environment.SetEnvironmentVariable does not propagate to native getenv()
    // on Linux/.NET 8+. We must call setenv directly for Godot's native code to see it.
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string name);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    static Engine()
    {
        // Windows: unload libgodot before loader shutdown, else its static destructors run under loader lock and
        // CoreMessaging.dll fail-fasts (0xE0464645). macOS aborts similarly but dyld pins ObjC images (fork TODO).
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            AppDomain.CurrentDomain.ProcessExit += (_, _) => UnloadLibGodot();
    }

    private static void UnloadLibGodot()
    {
        // A running instance still needs the library; its teardown at process
        // exit is safe because the display server was never destroyed.
        lock (ProcessSync)
        {
            if (_godotInstancePtr != IntPtr.Zero) return;
        }

        // No Godot calls past this point (ProcessExit). Prefer the recorded handle: with hosted multi-instance
        // every sweep must free exactly its own module.
        var handle = LibGodotLoader.LoadedLibraryHandle;
        if (handle != 0)
        {
            var attempts = 0;
            while (FreeLibrary(handle) && ++attempts < 32)
            {
            }
            return;
        }

        // The loaded module name is variant-specific (libgodot-editor.dll etc.);
        // when the resolver never ran, sweep all known names.
        string[] names = LibGodotLoader.LoadedLibraryFileName is { } loaded
            ? [loaded]
            : ["libgodot.dll", "libgodot-release.dll", "libgodot-debug.dll", "libgodot-editor.dll"];
        foreach (var name in names)
        {
            var module = GetModuleHandleW(name);
            var attempts = 0;
            while (module != IntPtr.Zero && FreeLibrary(module) && ++attempts < 32)
            {
                module = GetModuleHandleW(name);
            }
        }
    }

    public SceneTree Tree
    {
        get
        {
            EnsureRunning();
            return Godot.Engine.Singleton.GetMainLoop() as SceneTree ??
                   throw new NullReferenceException($"{nameof(Engine)}: Failed to get SceneTree.");
        }
    }

    /// <summary>
    /// Hosted mode: exact libgodot file for this load context, bypassing variant probing. When set, Start() also
    /// routes GodotPlugins through this context instead of the engine's hostfxr default-ALC load.
    /// </summary>
    public string? NativePath { get; init; }

    /// <summary>
    /// Preferred directory for the project's C# assembly (exported as GODOT_PROJECT_ASSEMBLY_DIR). Defaults to
    /// AppContext.BaseDirectory; hosted programs living elsewhere set it. Process-global, read during boot.
    /// </summary>
    public string? ProjectAssemblyDir { get; init; }

    /// <summary>
    /// How long Start() waits for the process-wide boot lock. Matches the hosting layer's boot-gate timeout:
    /// generous enough for a debug-native first boot on a loaded CI runner, exceeded only when a boot is stuck.
    /// </summary>
    public TimeSpan BootLockTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Name of the process-wide mutex serializing engine boots; public so hosts and tests can contend on it.
    /// </summary>
    public static string BootLockName => ProcessBootLock.Name;

    /// <summary>Full path of the libgodot this load context actually loaded, when known.</summary>
    public static string? LoadedNativePath => LibGodotLoader.LoadedLibraryPath;

    /// <summary>2dog package version (the engine assembly's version, e.g. 4.7.1.68).</summary>
    public static Version Version { get; } = typeof(Engine).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>Completes after native destruction and synchronous Exited handlers when this engine started.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Raised after this engine's native instance has been destroyed.</summary>
    public event Action? Exited;

    public void Dispose()
    {
        BeginDispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        BeginDispose();
        await Completion.ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void BeginDispose()
    {
        bool destroy;
        lock (_sync)
        {
            if (_state == LifecycleState.Disposed || _disposeRequested) return;
            EnsureOwnerThread();
            _disposeRequested = true;
            // Game callbacks can dispose their host during Start() or Iteration().
            // Never free the native engine while its stack is still executing.
            if (_state == LifecycleState.Starting || _iterationActive || _teardownStarted) return;
            destroy = _state is LifecycleState.Running or LifecycleState.Stopping;
            _state = destroy ? LifecycleState.Stopping : LifecycleState.Disposed;
        }

        if (!destroy)
        {
            CompleteExit();
            return;
        }

        BeginStartedInstanceDisposal();
    }

    private void BeginStartedInstanceDisposal()
    {
        if (OperatingSystem.IsBrowser())
        {
            try
            {
                WebHost.BeginShutdown(DestroyOwnedInstance);
            }
            catch
            {
                WebHost.ResetMainLoop();
                DestroyOwnedInstance();
                throw;
            }
            return;
        }

        DestroyOwnedInstance();
    }

    /// <summary>Starts Godot and returns a borrowed wrapper owned by this Engine.</summary>
    public GodotInstance Start()
    {
        GodotInstance instance;
        bool disposeAfterStart;
        lock (_sync)
        {
            if (_state != LifecycleState.Created)
                throw new InvalidOperationException($"{nameof(Engine)}: Start() may only be called once.");
            _ownerThreadId = System.Environment.CurrentManagedThreadId;
            _state = LifecycleState.Starting;

            try
            {
                if (OperatingSystem.IsBrowser())
                {
                    if (!WebHost.HasPluginsInitializer)
                        throw new InvalidOperationException(
                            $"{nameof(Engine)}: On browser, call {nameof(RegisterWebPluginsInitializer)}() with " +
                            "the game assembly's plugins-initializer pointer (see TwoDogWebBoot.cs in your web " +
                            "host folder, from the 2dog template; it must compile into the game project, which " +
                            "requires the LIBGODOT_ENABLED define) before Start().");
                    ThrowIfInstanceRunning();
                    instance = StartCore();
                }
                else
                {
                    using (ProcessBootLock.Acquire(BootLockTimeout))
                    {
                        ThrowIfInstanceRunning();
                        ConfigureGodotSharpDir(ProjectAssemblyDir);
                        instance = StartCore();
                    }
                }
            }
            catch
            {
                if (OperatingSystem.IsBrowser() && _ownedInstancePtr != IntPtr.Zero)
                {
                    _state = LifecycleState.Stopping;
                    BeginStartedInstanceDisposal();
                }
                else
                {
                    DestroyOwnedInstance();
                }
                throw;
            }

            disposeAfterStart = _disposeRequested;
            _state = disposeAfterStart ? LifecycleState.Disposed : LifecycleState.Running;
        }

        if (disposeAfterStart)
        {
            BeginStartedInstanceDisposal();
            throw new ObjectDisposedException(nameof(Engine), "The engine was disposed while Start() was in progress.");
        }

        return instance;
    }

    private void ThrowIfInstanceRunning()
    {
        lock (ProcessSync)
        {
            if (_godotInstancePtr != IntPtr.Zero || _destroyingInstance)
                throw new InvalidOperationException(
                    $"{nameof(Engine)}: A Godot instance is already running or shutting down. Only one instance " +
                    "may exist at a time - await the previous Engine.Completion before starting a new one.");
        }
    }

    private GodotInstance StartCore()
    {
        if (NativePath is { } nativePath)
        {
            if (OperatingSystem.IsBrowser())
                throw new PlatformNotSupportedException(
                    $"{nameof(Engine)}: {nameof(NativePath)} is not applicable on browser (statically linked).");
            var module = LibGodotLoader.LoadExact(nativePath);
            HostedGodotPlugins.Register(module);
        }
        else if (!OperatingSystem.IsBrowser())
        {
            // Register unconditionally: gd_mono's hostfxr fallback boots a second runtime under
            // self-contained hosts and needs a machine-wide .NET install.
            HostedGodotPlugins.Register(LibGodotLoader.EnsureLoaded());
        }

        Console.WriteLine($"{nameof(Engine)}: 2dog {Version}, starting Godot instance...");

        // Prepare arguments for Godot. The project path was made absolute at
        // construction, so CWD movement between then and now cannot redirect it.
        List<string> godotArgs = [_project];
        if (_projectPath is { } projectPath)
        {
            godotArgs.Add("--path");
            godotArgs.Add(projectPath);
        }

        godotArgs.AddRange(_args);

        // Create a Godot instance via P/Invoke (without starting)
        var instancePtr = CreateGodotInstance(godotArgs.ToArray());

        if (instancePtr == IntPtr.Zero)
            throw new NullReferenceException($"{nameof(Engine)}: Error creating Godot instance, returned IntPtr.Zero");

        var instanceConflict = false;
        lock (ProcessSync)
        {
            instanceConflict = _godotInstancePtr != IntPtr.Zero || _destroyingInstance;
            if (!instanceConflict) _godotInstancePtr = instancePtr;
        }
        if (instanceConflict)
        {
            LibGodot.libgodot_destroy_godot_instance(instancePtr);
            throw new InvalidOperationException($"{nameof(Engine)}: A Godot instance is already running or shutting down.");
        }
        lock (_sync) _ownedInstancePtr = instancePtr;

        Console.WriteLine($"{nameof(Engine)}: Godot instance created successfully!");

        // Call start() using our minimal binding
        if (!LibGodot.CallGodotInstanceStart(instancePtr))
        {
            Console.Error.WriteLine("Error starting Godot instance");
            throw new Exception($"{nameof(Engine)}: Error starting Godot instance");
        }

        // Get the GodotInstance object from the native pointer
        var godotInstance = LibGodot.GetGodotInstanceFromPtr(instancePtr);
        if (godotInstance == null)
        {
            Console.Error.WriteLine($"{nameof(Engine)}: Failed to get GodotInstance from pointer");
            throw new NullReferenceException($"{nameof(Engine)}: Failed to get GodotInstance from pointer.");
        }

        Console.WriteLine($"{nameof(Engine)}: Godot started successfully!");
        lock (_sync)
        {
            _godotInstance = godotInstance;
            _startedLifetime = true;
        }
        return godotInstance;
    }

    /// <summary>
    /// Points GODOTSHARP_DIR at GodotPlugins.dll (flat template layout, then editor's GodotSharp/Api/Debug) and
    /// GODOT_PROJECT_ASSEMBLY_DIR at the host output, whose game assembly matches the host config.
    /// </summary>
    internal static void ConfigureGodotSharpDir(string? projectAssemblyDir = null)
    {
        var baseDir = (projectAssemblyDir ?? AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrEmpty(baseDir))
            SetEnvironmentVariableForNative("GODOT_PROJECT_ASSEMBLY_DIR", baseDir);

        var assemblyDir = Path.GetDirectoryName(typeof(Engine).Assembly.Location);
        if (string.IsNullOrEmpty(assemblyDir)) return;

        var dir = assemblyDir;
        if (!File.Exists(Path.Combine(dir, "GodotPlugins.dll")))
        {
            dir = Path.Combine(assemblyDir, "GodotSharp", "Api", "Debug");
            if (!File.Exists(Path.Combine(dir, "GodotPlugins.dll"))) return;
        }

        SetEnvironmentVariableForNative("GODOTSHARP_DIR", dir);
    }

    private static void SetEnvironmentVariableForNative(string name, string value)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            setenv(name, value, 1);
        else
            System.Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>
    /// Registers the game's <c>GodotPlugins.Game.Main.InitializeFromGameProject</c> pointer for browser hosts
    /// (exposed by the template's <c>TwoDogWebBoot.cs</c>). Call before <see cref="Start"/>; throws on desktop.
    /// </summary>
    public static void RegisterWebPluginsInitializer(IntPtr initializer)
    {
        if (!OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException(
                $"{nameof(RegisterWebPluginsInitializer)} is only meaningful on browser (wasm) hosts.");
        WebHost.RegisterPluginsInitializer(initializer);
    }

    /// <summary>
    /// Browser only: whether Godot quitting also exits the wasm runtime (default false).
    /// Keeping the runtime alive supports sequential restarts: the instance is still destroyed
    /// and <see cref="Exited"/> raised, the page keeps running and a new <see cref="Engine"/> may be started -
    /// on a fresh canvas element (a browser reuses one WebGL context per element).
    /// </summary>
    public static bool WebExitRuntimeOnQuit
    {
        get => !OperatingSystem.IsBrowser() || WebHost.ExitRuntimeOnQuit;
        set
        {
            if (!OperatingSystem.IsBrowser())
                throw new PlatformNotSupportedException(
                    $"{nameof(WebExitRuntimeOnQuit)} is only meaningful on browser (wasm) hosts.");
            WebHost.ExitRuntimeOnQuit = value;
        }
    }

    /// <summary>
    /// Runs the main loop. Desktop: blocks until quit; dispose afterward. Browser: hands the loop to
    /// emscripten and returns immediately; async teardown destroys the instance on quit.
    /// </summary>
    /// <param name="perFrame">Optional callback invoked once per frame after the engine iteration.</param>
    public void Run(Action? perFrame = null)
    {
        EnsureRunning();
        if (_runActive || _iterationActive)
            throw new InvalidOperationException($"{nameof(Engine)}: The main loop is already running.");
        _runActive = true;

        if (OperatingSystem.IsBrowser())
        {
            try
            {
                WebHost.RunMainLoop(IterateCore, perFrame, DestroyOwnedInstance);
            }
            catch
            {
                _runActive = false;
                throw;
            }
            return;
        }

        try
        {
            while (!_disposeRequested && !IterateCore())
                perFrame?.Invoke();
        }
        finally
        {
            _runActive = false;
        }
    }

    /// <summary>Runs one engine frame. A true result means Godot requested exit; dispose this Engine afterward.</summary>
    public bool Iteration()
    {
        if (_runActive)
            throw new InvalidOperationException($"{nameof(Engine)}: Run() already owns the frame loop.");
        return IterateCore();
    }

    private bool IterateCore()
    {
        GodotInstance instance;
        lock (_sync)
        {
            EnsureActiveStateLocked();
            if (_iterationActive)
                throw new InvalidOperationException($"{nameof(Engine)}: Iteration() cannot be called from an engine frame.");
            instance = _godotInstance!;
        }
        EnsureOwnedInstance();

        _iterationActive = true;
        try
        {
            var quit = OperatingSystem.IsBrowser() ? WebHost.Iteration() : instance.Iteration();
            return quit || _disposeRequested;
        }
        finally
        {
            _iterationActive = false;
            if (_disposeRequested)
                BeginStartedInstanceDisposal();
        }
    }

    /// <summary>Requests a graceful quit. Repeated requests are harmless.</summary>
    public void RequestQuit()
    {
        lock (_sync)
        {
            if (_state is LifecycleState.Stopping or LifecycleState.Stopped or LifecycleState.Disposed) return;
            EnsureActiveStateLocked();
            _state = LifecycleState.Stopping;
        }

        try
        {
            RequestQuitCore();
        }
        catch
        {
            lock (_sync)
            {
                if (_state == LifecycleState.Stopping) _state = LifecycleState.Running;
            }
            throw;
        }
    }

    private void RequestQuitCore() => TreeForOwnedInstance().Quit();

    private SceneTree TreeForOwnedInstance()
    {
        IntPtr owned;
        lock (_sync)
            owned = _ownedInstancePtr;
        if (owned == IntPtr.Zero || owned != GetCurrentInstancePtr())
            throw new InvalidOperationException($"{nameof(Engine)}: This engine no longer owns the running instance.");
        return Godot.Engine.Singleton.GetMainLoop() as SceneTree ??
               throw new NullReferenceException($"{nameof(Engine)}: Failed to get SceneTree.");
    }

    private void EnsureRunning()
    {
        lock (_sync) EnsureActiveStateLocked();
        EnsureOwnedInstance();
    }

    private void EnsureActiveStateLocked()
    {
        EnsureOwnerThread();
        if (_state is not (LifecycleState.Running or LifecycleState.Stopping) ||
            _ownedInstancePtr == IntPtr.Zero || _godotInstance is null)
            throw new InvalidOperationException(
                $"{nameof(Engine)}: Start() must succeed and this engine must own the active instance.");
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != 0 && !Completion.IsCompleted &&
            _ownerThreadId != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException($"{nameof(Engine)}: Lifecycle calls must run on the thread that called Start().");
    }

    private void EnsureOwnedInstance()
    {
        IntPtr owned;
        lock (_sync) owned = _ownedInstancePtr;
        if (owned == IntPtr.Zero || owned != GetCurrentInstancePtr())
            throw new InvalidOperationException($"{nameof(Engine)}: This engine no longer owns the active instance.");
    }

    private static IntPtr GetCurrentInstancePtr()
    {
        lock (ProcessSync) return _godotInstancePtr;
    }

    private void DestroyOwnedInstance()
    {
        IntPtr instancePtr;
        lock (_sync)
        {
            if (_teardownStarted) return;
            _teardownStarted = true;
            instancePtr = _ownedInstancePtr;
            _ownedInstancePtr = IntPtr.Zero;
            _godotInstance = null;
            _state = _disposeRequested ? LifecycleState.Disposed : LifecycleState.Stopped;
        }

        var destroy = false;
        if (instancePtr != IntPtr.Zero)
        {
            lock (ProcessSync)
            {
                if (_godotInstancePtr == instancePtr)
                {
                    _destroyingInstance = true;
                    destroy = true;
                }
            }
        }

        if (destroy)
        {
            try
            {
                LibGodot.libgodot_destroy_godot_instance(instancePtr);
                Console.WriteLine($"{nameof(Engine)}: Godot instance destroyed.");
            }
            catch (Exception e)
            {
                _completion.TrySetException(e);
                throw;
            }
            finally
            {
                lock (ProcessSync)
                {
                    if (_godotInstancePtr == instancePtr) _godotInstancePtr = IntPtr.Zero;
                    _destroyingInstance = false;
                }
            }
        }
        CompleteExit();
    }

    private void CompleteExit()
    {
        lock (_sync)
        {
            if (_exitNotified || _completion.Task.IsCompleted) return;
            _exitNotified = true;
        }
        try
        {
            if (!_startedLifetime || Exited is not { } exited) return;
            foreach (Action handler in exited.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"{nameof(Engine)}: {nameof(Exited)} handler failed: {e}");
                }
            }
        }
        finally
        {
            _completion.TrySetResult();
        }
    }

    /// <summary>
    /// Resolves the Godot project directory from <c>[AssemblyMetadata("GodotProjectDir", "...")]</c> on loaded
    /// assemblies; the 2dog package emits it from the <c>&lt;GodotProjectDir&gt;</c> MSBuild property.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no loaded assembly has the <c>GodotProjectDir</c> metadata attribute.
    /// </exception>
    public static string ResolveProjectDir()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                {
                    if (attr.Key == "GodotProjectDir" && !string.IsNullOrEmpty(attr.Value))
                        return attr.Value;
                }
            }
            catch
            {
                // Some dynamic/reflection-emit assemblies may throw
            }
        }

        throw new InvalidOperationException(
            "GodotProjectDir not found. Set <GodotProjectDir> in your .csproj to the " +
            "path of the directory containing project.godot (relative to the .csproj).");
    }

    /// <summary>
    /// Resolves the <see cref="Engine"/> path argument: null when an exe-adjacent &lt;host&gt;.pck exists (published
    /// build, auto-loaded by the engine), otherwise <see cref="ResolveProjectDir"/> (source build).
    /// </summary>
    /// <exception cref="InvalidOperationException">No exe-adjacent .pck and no GodotProjectDir metadata.</exception>
    public static string? ResolveContent()
    {
        if (System.Environment.ProcessPath is { Length: > 0 } exePath &&
            File.Exists(Path.ChangeExtension(exePath, ".pck")))
            return null;
        return ResolveProjectDir();
    }

    private static unsafe IntPtr CreateGodotInstance(string[] args)
    {
        // Manual UTF-8 argv marshalling: the P/Invoke must stay fully
        // blittable for browser-wasm (see note in LibGodot.cs).
        var argv = new nint[args.Length];
        try
        {
            for (var i = 0; i < args.Length; i++)
                argv[i] = Marshal.StringToCoTaskMemUTF8(args[i]);

            fixed (nint* argvPtr = argv)
            {
                return LibGodot.libgodot_create_godot_instance(
                    args.Length,
                    argvPtr,
                    (nint)(delegate* unmanaged<nint, nint, GDExtensionInitialization*, byte>)&LibGodot.InitCallback
                );
            }
        }
        finally
        {
            foreach (var ptr in argv)
                Marshal.FreeCoTaskMem(ptr);
        }
    }
}
