// Phase 0/1 spike: prove that a .NET host can drive a NON-mono libgodot build
// entirely through the raw GDExtension interface - no GodotSharp anywhere.
//
// Phase 1 update: the interop plumbing now lives in twodog.bindings
// (generated interface layer + StringNames/NativeString/Variants/MethodBinds);
// this host keeps only what a host should own - loading libgodot, class
// registration for its one test class, and the checks themselves.
//
// Method hashes come from the official 4.7-stable extension_api.json;
// fork-only methods (GodotInstance.*) are called hash-free via variant_call.

using System.Runtime.InteropServices;
using Godot;
using Godot.NativeInterop;

namespace GdextSpike;

internal static unsafe class Program
{
    private const byte TRUE = 1;
    private const nint INSTANCE_TOKEN = 0xD06;

    // ---- method binds (resolved after instance creation / at SCENE level) ----
    private static nint _mbGodotInstanceStart;
    private static nint _mbEngineGetMainLoop;
    private static nint _mbEngineGetFps;
    private static nint _mbSceneTreeGetRoot;
    private static nint _mbNodeAddChild;
    private static nint _mbNodeSetName;
    private static nint _mbNodeGetChildCount;
    private static nint _mbNodeSetProcess;
    private static nint _mbClassDbInstantiate;
    private static nint _mbObjectGetClass;

    // ---- spike state observed from engine callbacks ----
    private static int _processCalls;
    private static double _lastDelta;
    private static bool _readyCalled;
    private static bool _freeInstanceCalled;
    private static bool _sceneLevelInitialized;

    private static int Main()
    {
        // .NET block-buffers stdout when redirected; the engine writes its own
        // stderr unbuffered, so force line-accurate interleaving.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        var repoRoot = FindRepoRoot();
        var dllPath = FindNonMonoLibgodot(repoRoot);
        Console.WriteLine($"[spike] libgodot: {dllPath}");

        var lib = NativeLibrary.Load(dllPath);
        var createGodotInstance = (delegate* unmanaged<int, nint*, nint, nint>)NativeLibrary.GetExport(lib, "libgodot_create_godot_instance");
        var destroyGodotInstance = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(lib, "libgodot_destroy_godot_instance");

        // Scene classes register in ClassDB at SCENE level (which runs inside
        // GodotInstance.start() under libgodot) - hook it before creating.
        GdExtensionHost.LevelInitialized += level =>
        {
            if (level != GDExtensionInitializationLevel.GDEXTENSION_INITIALIZATION_SCENE) return;
            _mbSceneTreeGetRoot = MethodBinds.Resolve("SceneTree", "get_root", 1757182445);
            _mbNodeAddChild = MethodBinds.Resolve("Node", "add_child", 3863233950);
            _mbNodeSetName = MethodBinds.Resolve("Node", "set_name", 3304788590);
            _mbNodeGetChildCount = MethodBinds.Resolve("Node", "get_child_count", 894402480);
            _mbNodeSetProcess = MethodBinds.Resolve("Node", "set_process", 2586408642);
            RegisterSpikeNode();
            _sceneLevelInitialized = true;
        };

        // -- create the instance --
        string[] args = ["gdext.spike", "--headless", "--path", Path.Combine(repoRoot, "spikes", "gdext.spike", "project")];
        var argv = new nint[args.Length];
        for (var i = 0; i < args.Length; i++) argv[i] = Marshal.StringToCoTaskMemUTF8(args[i]);

        nint instance;
        fixed (nint* argvPtr = argv)
        {
            instance = createGodotInstance(args.Length, argvPtr, GdExtensionHost.InitCallbackPointer);
        }
        foreach (var p in argv) Marshal.FreeCoTaskMem(p);

        Check(instance != 0, "libgodot_create_godot_instance returned an instance");
        Check(GdExtensionHost.Loaded, $"all {161} interface procs resolved (missing: {GdExtensionHost.MissingProcsDisplay})");
        MathLayout.Validate();
        Check(true, "math struct layouts match the engine's float_64 tables");

        // Core classes are resolvable from here on.
        _mbGodotInstanceStart = MethodBinds.Resolve("GodotInstance", "start", 2240911060);
        _mbEngineGetMainLoop = MethodBinds.Resolve("Engine", "get_main_loop", 1016888095);
        _mbEngineGetFps = MethodBinds.Resolve("Engine", "get_frames_per_second", 1740695150);
        _mbClassDbInstantiate = MethodBinds.Resolve("ClassDB", "instantiate", 2760726917);
        _mbObjectGetClass = MethodBinds.Resolve("Object", "get_class", 201670096);

        UtilityPrint("[spike] hello from raw GDExtension (printed by the engine)");

        // -- start() ptrcall; SCENE-level init (and our registration) runs inside --
        Check(MethodBinds.CallRet<byte>(_mbGodotInstanceStart, instance) == TRUE, "GodotInstance.start() returned true");
        Check(_sceneLevelInitialized, "initialize callback reached SCENE level (SpikeNode registered)");

        // -- singleton + object-returning ptrcalls --
        var snEngine = StringNames.Get("Engine").Opaque;
        var engineSingleton = GdExtensionInterface.GlobalGetSingleton((nint)(&snEngine));
        Check(engineSingleton != 0, "global_get_singleton(Engine)");

        var mainLoop = MethodBinds.CallRet<nint>(_mbEngineGetMainLoop, engineSingleton);
        Check(mainLoop != 0, "Engine.get_main_loop() returned SceneTree");

        var root = MethodBinds.CallRet<nint>(_mbSceneTreeGetRoot, mainLoop);
        Check(root != 0, "SceneTree.get_root() returned Window");

        var fps = MethodBinds.CallRet<double>(_mbEngineGetFps, engineSingleton);
        Check(fps >= 0, $"Engine.get_frames_per_second() = {fps}");

        // -- instantiate our extension class via ClassDB.instantiate (Variant-returning ptrcall) --
        var snClassDb = StringNames.Get("ClassDB").Opaque;
        var classDb = GdExtensionInterface.GlobalGetSingleton((nint)(&snClassDb));
        Check(classDb != 0, "global_get_singleton(ClassDB)");

        NativeVariant instantiated;
        var snSpikeNode = StringNames.Get("SpikeNode").Opaque;
        MethodBinds.Call(_mbClassDbInstantiate, classDb, [(nint)(&snSpikeNode)], &instantiated);
        var spikeNode = Variants.ToObject(in instantiated);
        Variants.Destroy(ref instantiated);
        Check(spikeNode != 0, "ClassDB.instantiate(&\"SpikeNode\") returned an object");

        // -- String-returning ptrcall: the engine sees our class identity --
        ulong classNameStr = 0;
        GdExtensionInterface.ObjectMethodBindPtrcall(_mbObjectGetClass, spikeNode, 0, (nint)(&classNameStr));
        var className = NativeString.ReadAndDestroy(ref classNameStr);
        Check(className == "SpikeNode", $"Object.get_class() = \"{className}\"");

        // -- ptrcalls with StringName / object / bool / enum arguments --
        var snNodeName = StringNames.Get("spike_node").Opaque;
        MethodBinds.Call(_mbNodeSetName, spikeNode, [(nint)(&snNodeName)]);

        byte boolTrue = TRUE;
        MethodBinds.Call(_mbNodeSetProcess, spikeNode, [(nint)(&boolTrue)]);

        long internalMode = 0; // Node.InternalMode.INTERNAL_MODE_DISABLED
        byte forceReadable = 0;
        MethodBinds.Call(_mbNodeAddChild, root, [(nint)(&spikeNode), (nint)(&forceReadable), (nint)(&internalMode)]);

        byte includeInternal = 0;
        long childCount = 0;
        MethodBinds.Call(_mbNodeGetChildCount, root, [(nint)(&includeInternal)], &childCount);
        Check(childCount >= 1, $"root.get_child_count() = {childCount} after add_child");

        // -- pump the loop via hash-free variant_call on the fork-only GodotInstance.iteration() --
        var instanceVariant = Variants.FromObject(instance);
        var snIteration = StringNames.Get("iteration");
        var quit = false;
        var frames = 0;
        while (!quit && _processCalls < 60 && frames < 600)
        {
            var ret = Variants.Call(ref instanceVariant, snIteration);
            quit = Variants.ToBool(in ret);
            Variants.Destroy(ref ret);
            frames++;
        }
        Variants.Destroy(ref instanceVariant);

        Check(_readyCalled, "_ready() virtual dispatched into C#");
        Check(_processCalls >= 60, $"_process() virtual dispatched {_processCalls} times (last delta = {_lastDelta:F4}s)");

        // -- RefCounted lifetime: the strong/weak GCHandle flip protocol --
        var rcId = RefCountedLifetimeChecks(classDb);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        DisposalQueue.Drain();
        Check(GdExtensionInterface.ObjectGetInstanceFromId(rcId) == 0,
            $"collected wrapper released its ref; RefCounted died on Drain() (released={DisposalQueue.Released})");
        Check(InstanceBindings.FreedBindings >= 1, "binding free callback fired on RefCounted death");

        // -- generated typed API (phase 2): 1036 classes from extension_api.json --
        Check(Godot.Engine.Singleton.NativePtr == engineSingleton, "typed Engine.Singleton wraps the same native pointer");

        var loop = Godot.Engine.GetMainLoop();
        Check(loop is SceneTree, $"GetMainLoop() materialized as most-derived type ({loop?.GetType().Name})");

        var tree = (SceneTree)loop!;
        var rootTyped = tree.Root;
        Check(rootTyped is not null && rootTyped.NativePtr == root, "typed SceneTree.Root is identity-equal with the raw-ptrcall root");

        var child = new Node();
        child.Name = "typed_child";
        rootTyped!.AddChild(child);
        Check(child.Name == "typed_child", $"typed Name property roundtrip (StringName return) = \"{child.Name}\"");
        Check(rootTyped.GetChildCount() == childCount + 1, "typed AddChild seen by typed GetChildCount");

        var node2d = new Node2D();
        node2d.Position = new Vector2(3.5f, -4.25f);
        var pos = node2d.Position;
        Check(pos.X == 3.5f && pos.Y == -4.25f, $"Vector2 arg/return roundtrip through generated ptrcall = {pos}");
        node2d.Free();
        Check(!node2d.IsValid, "typed Free() invalidates the wrapper (ObjectID check)");

        using (var res = new RefCounted())
        {
            Check(res.GetReferenceCount() == 1, $"new RefCounted() adopts construct3's refcount (rc == {res.GetReferenceCount()})");
        }
        var releasedBefore = DisposalQueue.Released;
        DisposalQueue.Drain();
        Check(DisposalQueue.Released == releasedBefore + 1, "disposed RefCounted released through the drain (no leak)");

        UtilityPrint($"[spike] engine-side goodbye after {frames} iterations");

        // -- teardown: the tree owns spike_node; destroying the instance must fire free_instance --
        destroyGodotInstance(instance);
        Check(_freeInstanceCalled, "free_instance callback fired during engine teardown");

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine($"[spike] PASS - all {_checks} checks succeeded. GodotSharp was never loaded.");
            return 0;
        }
        Console.WriteLine($"[spike] FAIL - {_failures}/{_checks} checks failed.");
        return 1;
    }

    /// <summary>
    /// Exercises the RefCounted wrapper protocol; NoInlining so the wrapper is
    /// provably unrooted when the caller runs GC. Returns the instance id for
    /// the post-collection death check.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ulong RefCountedLifetimeChecks(nint classDb)
    {
        // ClassDB.instantiate returns a Variant that holds one engine ref.
        NativeVariant rcVariant;
        var snRefCounted = StringNames.Get("RefCounted").Opaque;
        MethodBinds.Call(_mbClassDbInstantiate, classDb, [(nint)(&snRefCounted)], &rcVariant);
        var rcPtr = Variants.ToObject(in rcVariant); // borrowed while the variant lives

        // Wrapping a borrowed pointer takes the wrapper's own reference.
        var wrapper = InstanceBindings.GetOrCreate(rcPtr, refCounted: true, adoptRef: false)!;
        var rc = RefCountedNative.GetReferenceCount(rcPtr);
        Check(rc == 2, $"rc == 2 while variant + wrapper hold refs (got {rc})");
        Check(InstanceBindings.DebugIsStrong(rcPtr) == true, "binding handle STRONG while the engine holds a ref");

        Variants.Destroy(ref rcVariant); // engine-side unref fires the reference callback
        rc = RefCountedNative.GetReferenceCount(rcPtr);
        Check(rc == 1, $"rc == 1 after variant destroyed (got {rc})");
        Check(InstanceBindings.DebugIsStrong(rcPtr) == false, "binding handle flipped WEAK on the 2->1 edge");

        var again = InstanceBindings.GetOrCreate(rcPtr, refCounted: true, adoptRef: false);
        Check(ReferenceEquals(wrapper, again), "re-wrapping the same pointer returns the same managed instance");
        Check(wrapper.IsValid, "wrapper.IsValid (ObjectID-validated)");
        return wrapper.InstanceId;
    }

    // ------------------------------------------------- extension class --

    private static void RegisterSpikeNode()
    {
        var info = new GDExtensionClassCreationInfo6
        {
            is_exposed = TRUE,
            create_instance_func = (nint)(delegate* unmanaged<nint, byte, nint>)&CreateInstance,
            free_instance_func = (nint)(delegate* unmanaged<nint, nint, void>)&FreeInstance,
            get_virtual_call_data_func = (nint)(delegate* unmanaged<nint, nint, uint, nint>)&GetVirtualCallData,
            call_virtual_with_data_func = (nint)(delegate* unmanaged<nint, nint, nint, nint*, nint, void>)&CallVirtualWithData,
        };

        var snClass = StringNames.Get("SpikeNode").Opaque;
        var snParent = StringNames.Get("Node").Opaque;
        GdExtensionInterface.ClassdbRegisterExtensionClass6(GdExtensionHost.Library, (nint)(&snClass), (nint)(&snParent), (nint)(&info));
    }

    [UnmanagedCallersOnly]
    private static nint CreateInstance(nint classUserdata, byte notifyPostinitialize)
    {
        var snParent = StringNames.Get("Node").Opaque;
        var obj = GdExtensionInterface.ClassdbConstructObject3((nint)(&snParent));
        var snClass = StringNames.Get("SpikeNode").Opaque;
        GdExtensionInterface.ObjectSetInstance(obj, (nint)(&snClass), INSTANCE_TOKEN);
        return obj;
    }

    [UnmanagedCallersOnly]
    private static void FreeInstance(nint classUserdata, nint instance)
    {
        if (instance == INSTANCE_TOKEN) _freeInstanceCalled = true;
    }

    [UnmanagedCallersOnly]
    private static nint GetVirtualCallData(nint classUserdata, nint name, uint hash)
    {
        // StringNames are interned: equal names hold the same payload pointer.
        var payload = *(ulong*)name;
        if (payload == StringNames.Get("_process").Opaque) return 1;
        if (payload == StringNames.Get("_ready").Opaque) return 2;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void CallVirtualWithData(nint instance, nint name, nint userdata, nint* args, nint ret)
    {
        switch (userdata)
        {
            case 1: // _process(double delta)
                _lastDelta = *(double*)args[0];
                _processCalls++;
                break;
            case 2: // _ready()
                _readyCalled = true;
                Console.WriteLine("[spike] _ready() called from the engine");
                break;
        }
    }

    // ---------------------------------------------------------- utilities --

    private static int _checks;
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) _failures++;
        Console.WriteLine($"[spike] {(ok ? "ok " : "FAIL")} {what}");
    }

    private static nint _utilPrint;

    private static void UtilityPrint(string message)
    {
        if (_utilPrint == 0)
        {
            var snPrint = StringNames.Get("print").Opaque;
            _utilPrint = GdExtensionInterface.VariantGetPtrUtilityFunction((nint)(&snPrint), 2648703342);
        }
        var v = Variants.FromString(message);
        nint* args = stackalloc nint[1];
        args[0] = (nint)(&v);
        ((delegate* unmanaged<nint, nint*, int, void>)_utilPrint)(0, args, 1);
        Variants.Destroy(ref v);
    }

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
                .Where(f => !f.Contains(".mono.")).ToArray()
            : [];
        if (candidates.Length == 0)
            throw new FileNotFoundException(
                "No non-mono template_debug libgodot found in godot/bin. " +
                "Build it with: uv run python build-godot.py --mono no --no-editor --no-glue --target template_debug");
        return candidates[0];
    }
}
