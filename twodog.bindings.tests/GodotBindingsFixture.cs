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

        string[] args = ["twodog.bindings.tests", "--headless", "--path", Path.Combine(RepoRoot, "twodog.bindings.tests", "project")];
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

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "2dog.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate 2dog repo root above " + AppContext.BaseDirectory);
    }

    private static string FindNonMonoLibgodot(string repoRoot)
    {
        var binDir = Path.Combine(repoRoot, "godot", "bin");
        var candidates = Directory.Exists(binDir)
            ? Directory.GetFiles(binDir, "*template_debug*shared_library*.dll")
                .Where(f => !f.Contains(".mono.") && !f.Contains(".console.")).ToArray()
            : [];
        if (candidates.Length == 0)
            throw new FileNotFoundException(
                "No non-mono template_debug libgodot found in godot/bin. " +
                "Build it with: uv run python build-godot.py --mono no --no-editor --no-glue --target template_debug");
        return candidates[0];
    }
}

[CollectionDefinition(nameof(GodotBindingsCollection), DisableParallelization = true)]
public class GodotBindingsCollection : ICollectionFixture<GodotBindingsFixture>;
