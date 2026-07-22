using System.Runtime.InteropServices;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

/// <summary>
/// Boots a NON-mono libgodot once for the whole test run (Godot allows one
/// instance per process) and starts it, so SCENE-level classes are registered
/// before any test runs. All engine-touching tests join
/// <see cref="GodotBindingsCollection"/> and therefore run sequentially.
/// </summary>
public sealed unsafe class GodotBindingsFixture : IDisposable
{
    public nint InstancePtr { get; }
    public string RepoRoot { get; }

    private readonly delegate* unmanaged<nint, void> _destroyGodotInstance;
    private static string? _loadedLibraryPath;

    public GodotBindingsFixture()
    {
        RepoRoot = FindRepoRoot();
        var dllPath = FindNonMonoLibgodot(RepoRoot);
        _loadedLibraryPath = dllPath;

        var lib = NativeLibrary.Load(dllPath);
        var create = (delegate* unmanaged<int, nint*, nint, nint>)NativeLibrary.GetExport(lib, "libgodot_create_godot_instance");
        _destroyGodotInstance = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(lib, "libgodot_destroy_godot_instance");

        var projectDir = Path.Combine(RepoRoot, "twodog.bindings.tests", "project");
        EnsureGlobalScriptClassCache(projectDir);
        string[] args = ["twodog.bindings.tests", "--headless", "--path", projectDir];
        var argv = new nint[args.Length];
        for (var i = 0; i < args.Length; i++) argv[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
        try
        {
            fixed (nint* argvPtr = argv)
            {
                InstancePtr = create(args.Length, argvPtr, GdExtensionHost.InitCallbackPointer);
            }
        }
        finally
        {
            foreach (var p in argv) Marshal.FreeCoTaskMem(p);
        }

        if (InstancePtr == 0) throw new InvalidOperationException("libgodot_create_godot_instance failed");
        if (!GdExtensionHost.Loaded)
            throw new InvalidOperationException("Missing GDExtension procs: " + GdExtensionHost.MissingProcsDisplay);

        // start(): runs SCENE-level extension initialization (libgodot lifecycle).
        var start = MethodBinds.Resolve("GodotInstance", "start", 2240911060);
        if (MethodBinds.CallRet<byte>(start, InstancePtr) == 0)
            throw new InvalidOperationException("GodotInstance.start() failed");

        // Unload libgodot before loader shutdown: leaving it mapped crashes
        // the process on Windows with 0xE0464645 (same mitigation as
        // twodog.engine/Engine.cs). Must not call any Godot API in here.
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            if (!OperatingSystem.IsWindows() || _loadedLibraryPath is null) return;
            var module = GetModuleHandleW(Path.GetFileName(_loadedLibraryPath));
            var attempts = 0;
            while (module != 0 && FreeLibrary(module) && ++attempts < 32)
            {
                module = GetModuleHandleW(Path.GetFileName(_loadedLibraryPath));
            }
        };
    }

    /// <summary>Pumps engine iterations (hash-free variant_call on the fork-only GodotInstance.iteration).</summary>
    public void PumpFrames(int count)
    {
        var instanceVariant = Variants.FromObject(InstancePtr);
        var iteration = StringNames.Get("iteration");
        for (var i = 0; i < count; i++)
        {
            var ret = Variants.Call(ref instanceVariant, iteration);
            Variants.Destroy(ref ret);
        }
        Variants.Destroy(ref instanceVariant);
    }

    public void Dispose()
    {
        DisposalQueue.Drain();
        _destroyGodotInstance(InstancePtr);
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string moduleName);

    [DllImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);

    /// <summary>
    /// The minimal test project ships without a .godot import cache, which
    /// costs an engine error print at boot. Write the empty cache an editor
    /// import would produce for a project with no class_name scripts.
    /// </summary>
    private static void EnsureGlobalScriptClassCache(string projectDir)
    {
        var cache = Path.Combine(projectDir, ".godot", "global_script_class_cache.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        if (!File.Exists(cache)) File.WriteAllText(cache, "list=[]\n");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "2dog.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate 2dog repo root above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Variant under test: TWODOG_VARIANT env ('release', 'debug', 'editor'),
    /// default 'debug'. Prefers the gdext-suffixed natives; falls back to a
    /// plain non-mono shared_library build (pre-suffix local builds).
    /// </summary>
    private static string FindNonMonoLibgodot(string repoRoot)
    {
        var binDir = Path.Combine(repoRoot, "godot", "bin");
        var variant = Environment.GetEnvironmentVariable("TWODOG_VARIANT") switch
        {
            "release" => "template_release",
            "editor" => "editor",
            _ => "template_debug",
        };
        var ext = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        foreach (var suffix in (string[])["gdext_shared_library", "shared_library"])
        {
            var candidates = Directory.Exists(binDir)
                ? Directory.GetFiles(binDir, $"*godot.*.{variant}.*{suffix}{ext}")
                    .Where(f => !f.Contains(".mono.") && !f.Contains(".console.")).ToArray()
                : [];
            if (candidates.Length > 0) return candidates[0];
        }
        throw new FileNotFoundException(
            $"No non-mono {variant} libgodot found in godot/bin. " +
            $"Build it with: uv run python build-godot.py --mono no --no-editor --no-glue --target {variant}");
    }
}

[CollectionDefinition(nameof(GodotBindingsCollection), DisableParallelization = true)]
public class GodotBindingsCollection : ICollectionFixture<GodotBindingsFixture>;

/// <summary>
/// Scoped mute for engine output a test provokes on purpose (error macros,
/// printerr, print_line), so a clean run stays clean.
/// </summary>
public readonly struct EngineOutputMute : IDisposable
{
    public EngineOutputMute()
    {
        Godot.Engine.PrintErrorMessages = false;
        Godot.Engine.PrintToStdout = false;
    }

    public void Dispose()
    {
        Godot.Engine.PrintErrorMessages = true;
        Godot.Engine.PrintToStdout = true;
    }
}
