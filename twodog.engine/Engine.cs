using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;

namespace twodog;

public class Engine(string project, string? path = null, params string[] args) : IDisposable
{
    private static IntPtr _godotInstancePtr = IntPtr.Zero;

    // Only the Engine that successfully started the (process-wide) Godot
    // instance may destroy it. This keeps `using var engine = ...; engine.Start()`
    // patterns safe: disposing an Engine whose Start() failed or was never
    // called must not tear down an instance started by another Engine.
    private bool _ownsInstance;

    private GodotInstance? _godotInstance;

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
        // On Windows, unload libgodot before the OS starts process teardown.
        // If libgodot is still loaded when the process exits, its static
        // destructors run inside LdrShutdownProcess (DLL_PROCESS_DETACH, under
        // loader lock) and the Windows input stack fail-fasts in
        // CoreMessaging.dll (exit code 0xE0464645). godot.exe never hits this
        // because an executable's static destructors run during normal CRT
        // exit, before loader shutdown - unloading here restores that timing.
        //
        // macOS has a similar teardown problem with different symptoms: after
        // a clean shutdown, libgodot's exit-time static destructors abort with
        // "std::__1::system_error: mutex lock failed: Invalid argument"
        // (SIGABRT). The unload trick cannot be ported: libgodot contains
        // Objective-C classes, and dyld permanently pins such images - dlclose
        // never unloads them (verified: identical crash with a dlclose sweep).
        // Needs a fix in the fork's destructor chain instead; the desktop
        // smoke CI tolerates the known abort on macOS until then.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            AppDomain.CurrentDomain.ProcessExit += (_, _) => UnloadLibGodot();
    }

    private static void UnloadLibGodot()
    {
        // A running instance still needs the library; its teardown at process
        // exit is safe because the display server was never destroyed.
        if (_godotInstancePtr != IntPtr.Zero) return;

        // No Godot call may be made after this point (we are in ProcessExit).
        // Prefer the recorded handle: with hosted multi-instance (one module
        // per AssemblyLoadContext) every sweep must free exactly its own module.
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

    public SceneTree Tree => Godot.Engine.Singleton.GetMainLoop() as SceneTree ??
                             throw new NullReferenceException($"{nameof(Engine)}: Failed to get SceneTree.");

    /// <summary>
    /// Hosted mode: exact libgodot file to load for this load context, bypassing variant
    /// probing. Set by multi-instance hosts (2dog.hosting hands each instance its own pooled
    /// copy); when set, Start() also routes GodotPlugins through this context instead of the
    /// engine's hostfxr default-ALC load, keeping all managed engine state per-instance.
    /// </summary>
    public string? NativePath { get; init; }

    /// <summary>
    /// Directory the engine should prefer when loading the project's C# assembly
    /// (exported as GODOT_PROJECT_ASSEMBLY_DIR). Defaults to AppContext.BaseDirectory,
    /// which is only right when the game assembly ships with the outer host - hosted
    /// programs living elsewhere set this (2dog.hosting passes the program assembly's
    /// directory). The variable is process-global; multi-instance hosts serialize
    /// boots, and the engine reads it during instance creation.
    /// </summary>
    public string? ProjectAssemblyDir { get; init; }

    /// <summary>
    /// Absolute project path, captured at construction: boot-time CWD churn
    /// (another instance's boot or DirAccess moving the process CWD) must not
    /// change which project a relative path resolves to.
    /// </summary>
    private readonly string? _projectPath =
        string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);

    /// <summary>
    /// How long Start() waits for the process-wide boot lock that serializes
    /// engine boots (they mutate the process CWD and environment). Matches the
    /// hosting layer's boot-gate timeout: generous enough for a debug-native
    /// first boot on a loaded CI runner, so it is only ever exceeded when
    /// another boot in this process is genuinely stuck.
    /// </summary>
    public TimeSpan BootLockTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Name of the process-wide named mutex that serializes engine boots.
    /// Public so hosts and tests can observe or contend on it deliberately.
    /// </summary>
    public static string BootLockName => ProcessBootLock.Name;

    /// <summary>Full path of the libgodot this load context actually loaded, when known.</summary>
    public static string? LoadedNativePath => LibGodotLoader.LoadedLibraryPath;


    public void Dispose()
    {
        if (!_ownsInstance || _godotInstancePtr == IntPtr.Zero) return;
        // On web, emscripten owns the main loop after Run(); the instance is
        // destroyed by the engine's own quit flow (WebHost.ExitCallback), not
        // by the host disposing on the way out of Main().
        if (OperatingSystem.IsBrowser() && WebHost.MainLoopActive) return;
        _ownsInstance = false;
        Destroy();
    }

    public GodotInstance Start()
    {
        ThrowIfInstanceRunning();

        if (OperatingSystem.IsBrowser())
        {
            // No filesystem hosting on web: the game's plugins initializer
            // function pointer must have been registered up front (there is
            // no GodotPlugins.dll to load). One statically linked instance per
            // page - nothing to serialize, and wasm has no named mutexes.
            if (!WebHost.HasPluginsInitializer)
                throw new InvalidOperationException(
                    $"{nameof(Engine)}: On browser, call {nameof(RegisterWebPluginsInitializer)}() with " +
                    "the game assembly's plugins-initializer pointer (see TwoDogWebBoot.cs in your web " +
                    "host folder, from the 2dog template; it must compile into the game project, which " +
                    "requires the LIBGODOT_ENABLED define) before Start().");
            return StartCore();
        }

        // Boot mutates process-global state: the environment variables written
        // below are read during instance creation, and Godot's --path handling
        // moves the process CWD. Engines in other load contexts run their own
        // copy of this class, so the serialization must be OS-level.
        using (ProcessBootLock.Acquire(BootLockTimeout))
        {
            ThrowIfInstanceRunning();
            ConfigureGodotSharpDir(ProjectAssemblyDir);
            return StartCore();
        }
    }

    private void ThrowIfInstanceRunning()
    {
        if (_godotInstancePtr != IntPtr.Zero)
            throw new InvalidOperationException(
                $"{nameof(Engine)}: A Godot instance is already running. Only one instance may exist at a time " +
                "(a Godot limitation) - dispose the previous Engine before starting a new one.");
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

        Console.WriteLine("Starting Godot instance...");

        // Prepare arguments for Godot. The project path was made absolute at
        // construction, so CWD movement between then and now cannot redirect it.
        List<string> godotArgs = [project];
        if (_projectPath is { } projectPath)
        {
            godotArgs.Add("--path");
            godotArgs.Add(projectPath);
        }

        godotArgs.AddRange(args);

        // Create a Godot instance via P/Invoke (without starting)
        _godotInstancePtr = CreateGodotInstance(godotArgs.ToArray());

        if (_godotInstancePtr == IntPtr.Zero)
            throw new NullReferenceException($"{nameof(Engine)}: Error creating Godot instance, returned IntPtr.Zero");

        Console.WriteLine($"{nameof(Engine)}: Godot instance created successfully!");

        // Call start() using our minimal binding
        if (!LibGodot.CallGodotInstanceStart(_godotInstancePtr))
        {
            Console.Error.WriteLine("Error starting Godot instance");
            Destroy();
            throw new Exception($"{nameof(Engine)}: Error starting Godot instance");
        }

        // Get the GodotInstance object from the native pointer
        var godotInstance = LibGodot.GetGodotInstanceFromPtr(_godotInstancePtr);
        if (godotInstance == null)
        {
            Console.Error.WriteLine($"{nameof(Engine)}: Failed to get GodotInstance from pointer");
            Destroy();
            throw new NullReferenceException($"{nameof(Engine)}: Failed to get GodotInstance from pointer.");
        }

        Console.WriteLine($"{nameof(Engine)}: Godot started successfully!");
        _ownsInstance = true;
        _godotInstance = godotInstance;
        return godotInstance;
    }

    /// <summary>
    /// Points GODOTSHARP_DIR at the directory containing GodotPlugins.dll.
    /// When the host process is not in the output directory (e.g. dotnet test
    /// uses /usr/share/dotnet/dotnet), Godot's exe_dir fallback won't work.
    /// Checks the flat layout (template variants) first, then the nested
    /// GodotSharp/Api/Debug/ layout (editor variant). Also points
    /// GODOT_PROJECT_ASSEMBLY_DIR at the host's base directory: the host
    /// references the game project, so its output carries a game assembly
    /// matching the host's build configuration - libgodot prefers it over
    /// .godot/mono/temp/bin/&lt;config&gt;, which does not exist for
    /// configurations the game project was never built with directly
    /// (e.g. a Release-only publish). Must use native setenv on Unix because
    /// .NET's SetEnvironmentVariable doesn't propagate to native getenv() on
    /// Linux/.NET 8+.
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
    /// Registers the game's GodotPlugins initializer for browser (wasm) hosts.
    /// Pass a pointer to the source-generated
    /// <c>GodotPlugins.Game.Main.InitializeFromGameProject</c> in the game
    /// assembly, exposed by the template's <c>TwoDogWebBoot.cs</c> when the
    /// game project is compiled with the <c>LIBGODOT_ENABLED</c> define.
    /// Must be called before
    /// <see cref="Start"/>. No-op requirement on desktop (throws there to
    /// catch misuse early).
    /// </summary>
    public static void RegisterWebPluginsInitializer(IntPtr initializer)
    {
        if (!OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException(
                $"{nameof(RegisterWebPluginsInitializer)} is only meaningful on browser (wasm) hosts.");
        WebHost.RegisterPluginsInitializer(initializer);
    }

    /// <summary>
    /// Runs the engine main loop.
    /// Desktop: blocks, iterating the engine until it requests quit, then
    /// returns (the caller still owns disposal).
    /// Browser: hands the loop to emscripten and returns immediately; the
    /// engine keeps running via per-frame callbacks and destroys itself on
    /// quit. Do not Dispose() after Run() on the browser.
    /// </summary>
    /// <param name="perFrame">Optional callback invoked once per frame before
    /// the engine iteration.</param>
    public void Run(Action? perFrame = null)
    {
        if (_godotInstance == null || _godotInstancePtr == IntPtr.Zero)
            throw new InvalidOperationException($"{nameof(Engine)}: Start() must succeed before Run().");

        if (OperatingSystem.IsBrowser())
        {
            WebHost.RunMainLoop(perFrame, () =>
            {
                _ownsInstance = false;
                Destroy();
            });
            return;
        }

        while (!_godotInstance.Iteration())
        {
            perFrame?.Invoke();
        }
    }

    private static void Destroy()
    {
        LibGodot.libgodot_destroy_godot_instance(_godotInstancePtr);
        Console.WriteLine($"{nameof(Engine)}: Godot instance destroyed.");
        _godotInstancePtr = IntPtr.Zero;
    }

    /// <summary>
    /// Resolves the Godot project directory from <c>[AssemblyMetadata("GodotProjectDir", "...")]</c>
    /// on loaded assemblies. This attribute is emitted automatically at build time when the
    /// consuming project sets the <c>&lt;GodotProjectDir&gt;</c> MSBuild property and references
    /// the 2dog NuGet package.
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
    /// Resolves what to pass as the <see cref="Engine"/> path argument. Returns null when a
    /// pack file named after the running executable sits next to it (a published build - the
    /// engine auto-loads an exe-adjacent .pck; desktop publishes export one as
    /// &lt;host&gt;.pck), otherwise the project directory from
    /// <see cref="ResolveProjectDir"/> (a source build running from raw assets).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no exe-adjacent .pck exists and no loaded assembly has the
    /// <c>GodotProjectDir</c> metadata attribute.
    /// </exception>
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