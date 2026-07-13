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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            AppDomain.CurrentDomain.ProcessExit += (_, _) => UnloadLibGodot();
    }

    private static void UnloadLibGodot()
    {
        // A running instance still needs the library; its teardown at process
        // exit is safe because the display server was never destroyed.
        if (_godotInstancePtr != IntPtr.Zero) return;

        // No Godot call may be made after this point (we are in ProcessExit).
        var module = GetModuleHandleW("libgodot.dll");
        var attempts = 0;
        while (module != IntPtr.Zero && FreeLibrary(module) && ++attempts < 32)
        {
            module = GetModuleHandleW("libgodot.dll");
        }
    }

    public SceneTree Tree => Godot.Engine.Singleton.GetMainLoop() as SceneTree ??
                             throw new NullReferenceException($"{nameof(Engine)}: Failed to get SceneTree.");


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
        if (_godotInstancePtr != IntPtr.Zero)
            throw new InvalidOperationException(
                $"{nameof(Engine)}: A Godot instance is already running. Only one instance may exist at a time " +
                "(a Godot limitation) - dispose the previous Engine before starting a new one.");

        if (OperatingSystem.IsBrowser())
        {
            // No filesystem hosting on web: the game's plugins initializer
            // function pointer must have been registered up front (there is
            // no GodotPlugins.dll to load).
            if (!WebHost.HasPluginsInitializer)
                throw new InvalidOperationException(
                    $"{nameof(Engine)}: On browser, call {nameof(RegisterWebPluginsInitializer)}() with " +
                    "GodotPlugins.Game.Main.GetInitializePointer() (source-generated into the game " +
                    "assembly; requires the LIBGODOT_ENABLED define) before Start().");
        }
        else
        {
            // Ensure GODOTSHARP_DIR points to the directory containing GodotPlugins.dll.
            // When the host process is not in the output directory (e.g. dotnet test
            // uses /usr/share/dotnet/dotnet), Godot's exe_dir fallback won't work.
            // Must use native setenv on Unix because .NET's SetEnvironmentVariable
            // doesn't propagate to native getenv() on Linux/.NET 8+.
            var assemblyDir = Path.GetDirectoryName(typeof(Engine).Assembly.Location);
            if (assemblyDir != null && File.Exists(Path.Combine(assemblyDir, "GodotPlugins.dll")))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    setenv("GODOTSHARP_DIR", assemblyDir, 1);
                else
                    System.Environment.SetEnvironmentVariable("GODOTSHARP_DIR", assemblyDir);
            }
        }

        Console.WriteLine("Starting Godot instance...");

        // Prepare arguments for Godot
        List<string> godotArgs = [project];
        if (!string.IsNullOrEmpty(path))
        {
            godotArgs.Add("--path");
            godotArgs.Add(path);
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
    /// Registers the game's GodotPlugins initializer for browser (wasm) hosts.
    /// Pass the value of <c>GodotPlugins.Game.Main.GetInitializePointer()</c>,
    /// which is source-generated into the game assembly when it is compiled
    /// with the <c>LIBGODOT_ENABLED</c> define. Must be called before
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