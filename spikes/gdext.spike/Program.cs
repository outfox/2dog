// Phase 0 spike: prove that a .NET host can drive a NON-mono libgodot build
// entirely through the raw GDExtension interface - no GodotSharp anywhere.
//
// What it proves, in order:
//   1. proc loading via get_proc_address (the 4.1+ interface contract)
//   2. builtin construction/destruction over opaque storage (String, StringName, Variant)
//   3. utility function ptrcall (vararg `print` with a Variant argument)
//   4. singleton access + method-bind ptrcall with hashes from extension_api.json
//      (double return, object return, object/bool/enum args, String return)
//   5. registering an extension class (SpikeNode : Node) via classdb_register_extension_class6
//   6. instantiating it through ClassDB.instantiate (Variant-returning ptrcall)
//   7. virtual dispatch from the engine into C# (_ready/_process) via
//      get_virtual_call_data / call_virtual_with_data
//   8. dynamic (hash-free) variant_call for fork-only methods (GodotInstance.iteration)
//   9. clean engine teardown (free_instance callback observed, process exits 0)
//
// Method hashes come from the official 4.7-stable extension_api.json (godot-cpp);
// hashes are stable within a minor version. Fork-only methods (GodotInstance.*)
// are invoked via variant_call, which needs no hash - except start(), whose hash
// is already known-good from twodog.engine/LibGodot.cs.

using System.Runtime.InteropServices;
using System.Text;

namespace GdextSpike;

internal static unsafe class Program
{
    // ---- opaque builtin sizes, build config float_64 (win-x64, single precision) ----
    private const int VARIANT_SIZE = 24;

    private const byte TRUE = 1;

    // GDExtensionVariantType
    private const int TYPE_BOOL = 1;
    private const int TYPE_STRING = 4;
    private const int TYPE_OBJECT = 24;

    // GDExtensionInitializationLevel
    private const int LEVEL_SCENE = 2;

    // ---- libgodot entry points ----
    private static delegate* unmanaged<int, nint*, nint, nint> _createGodotInstance;
    private static delegate* unmanaged<nint, void> _destroyGodotInstance;

    // ---- GDExtension interface procs ----
    private static delegate* unmanaged<nint, nint> _getProcAddress;
    private static nint _library;

    private static delegate* unmanaged<nint, nint, byte, void> _stringNameNewLatin1;
    private static delegate* unmanaged<nint, nint, void> _stringNewUtf8;
    private static delegate* unmanaged<nint, nint, long, long> _stringToUtf8Chars;
    private static delegate* unmanaged<int, nint> _getVariantFromTypeCtor;   // returns fn(variant*, value*)
    private static delegate* unmanaged<int, nint> _getVariantToTypeCtor;     // returns fn(value*, variant*)
    private static delegate* unmanaged<nint, void> _variantDestroy;
    private static delegate* unmanaged<nint, nint, nint*, long, nint, CallError*, void> _variantCall;
    private static delegate* unmanaged<nint, long, nint> _variantGetPtrUtilityFunction;
    private static delegate* unmanaged<int, nint> _variantGetPtrDestructor;
    private static delegate* unmanaged<nint, nint> _globalGetSingleton;
    private static delegate* unmanaged<nint, nint, long, nint> _classdbGetMethodBind;
    private static delegate* unmanaged<nint, nint, nint*, nint, void> _methodBindPtrcall;
    private static delegate* unmanaged<nint, nint> _classdbConstructObject3;
    private static delegate* unmanaged<nint, nint, nint, void> _objectSetInstance;
    private static delegate* unmanaged<nint, nint, nint, nint, void> _classdbRegisterClass6;
    private static delegate* unmanaged<nint, ulong> _objectGetInstanceId;

    [StructLayout(LayoutKind.Sequential)]
    private struct GDExtensionInitialization
    {
        public int minimum_initialization_level;
        public nint userdata;
        public nint initialize;
        public nint deinitialize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CallError
    {
        public int error;
        public int argument;
        public int expected;
    }

    // GDExtensionClassCreationInfo6 - member order verified against
    // godot/core/extension/gdextension_interface.json (fork, 4.7.1).
    [StructLayout(LayoutKind.Sequential)]
    private struct ClassCreationInfo6
    {
        public byte is_virtual;
        public byte is_abstract;
        public byte is_exposed;
        public byte is_runtime;
        public nint icon_path;
        public nint set_func;
        public nint get_func;
        public nint get_property_list_func;
        public nint free_property_list_func;
        public nint property_can_revert_func;
        public nint property_get_revert_func;
        public nint validate_property_func;
        public nint notification_func;
        public nint to_string_func;
        public nint reference_func;
        public nint unreference_func;
        public nint create_instance_func;      // GDExtensionClassCreateInstance3
        public nint free_instance_func;        // GDExtensionClassFreeInstance
        public nint recreate_instance_func;
        public nint get_virtual_func;
        public nint get_virtual_call_data_func; // GDExtensionClassGetVirtualCallData2
        public nint call_virtual_with_data_func;
        public nint class_userdata;
    }

    // ---- persistent StringNames (opaque payload is one pointer; created is_static=1) ----
    private static ulong _snSpikeNode, _snNode, _snProcess, _snReady, _snIteration;

    // ---- method binds (resolved once at init) ----
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

    private static nint _utilPrint;

    // ---- spike state observed from engine callbacks ----
    private static int _processCalls;
    private static double _lastDelta;
    private static bool _readyCalled;
    private static bool _freeInstanceCalled;
    private static bool _sceneLevelInitialized;

    private const nint INSTANCE_TOKEN = 0xD06;

    private static int Main()
    {
        // .NET block-buffers stdout when redirected; the engine writes its own
        // stderr unbuffered, so force line-accurate interleaving.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        var repoRoot = FindRepoRoot();
        var dllPath = FindNonMonoLibgodot(repoRoot);
        Console.WriteLine($"[spike] libgodot: {dllPath}");

        var lib = NativeLibrary.Load(dllPath);
        _createGodotInstance = (delegate* unmanaged<int, nint*, nint, nint>)NativeLibrary.GetExport(lib, "libgodot_create_godot_instance");
        _destroyGodotInstance = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(lib, "libgodot_destroy_godot_instance");

        // -- create the instance; our init callback loads procs and registers SpikeNode --
        string[] args = ["gdext.spike", "--headless", "--path", Path.Combine(repoRoot, "spikes", "gdext.spike", "project")];
        var argv = new nint[args.Length];
        for (var i = 0; i < args.Length; i++) argv[i] = Marshal.StringToCoTaskMemUTF8(args[i]);

        nint instance;
        fixed (nint* argvPtr = argv)
        {
            instance = _createGodotInstance(args.Length, argvPtr,
                (nint)(delegate* unmanaged<nint, nint, GDExtensionInitialization*, byte>)&InitCallback);
        }
        foreach (var p in argv) Marshal.FreeCoTaskMem(p);

        Check(instance != 0, "libgodot_create_godot_instance returned an instance");

        // -- utility `print` with a Variant argument (engine-side print) --
        UtilityPrint("[spike] hello from raw GDExtension (printed by the engine)");

        // -- start() ptrcall (known-good hash from LibGodot.cs) --
        byte started = 0;
        _methodBindPtrcall(_mbGodotInstanceStart, instance, null, (nint)(&started));
        Check(started == TRUE, "GodotInstance.start() returned true");
        // NOTE: with libgodot, extension SCENE-level init runs during start(),
        // not during create - a load-bearing lifecycle fact for the bindings.
        Check(_sceneLevelInitialized, "initialize callback reached SCENE level (SpikeNode registered)");

        // -- singleton + object-returning ptrcalls: Engine.get_main_loop() -> SceneTree.get_root() --
        var engineSingleton = _globalGetSingleton(Sn(ref _tmpSn, "Engine"));
        Check(engineSingleton != 0, "global_get_singleton(Engine)");

        nint mainLoop = 0;
        _methodBindPtrcall(_mbEngineGetMainLoop, engineSingleton, null, (nint)(&mainLoop));
        Check(mainLoop != 0, "Engine.get_main_loop() returned SceneTree");

        nint root = 0;
        _methodBindPtrcall(_mbSceneTreeGetRoot, mainLoop, null, (nint)(&root));
        Check(root != 0, "SceneTree.get_root() returned Window");

        // -- double-returning ptrcall --
        double fps = -1;
        _methodBindPtrcall(_mbEngineGetFps, engineSingleton, null, (nint)(&fps));
        Check(fps >= 0, $"Engine.get_frames_per_second() = {fps}");

        // -- instantiate our extension class via ClassDB.instantiate (Variant-returning ptrcall) --
        var classDb = _globalGetSingleton(Sn(ref _tmpSn2, "ClassDB"));
        Check(classDb != 0, "global_get_singleton(ClassDB)");

        var retVariant = stackalloc byte[VARIANT_SIZE];
        nint* oneArg = stackalloc nint[1];
        fixed (ulong* pSn = &_snSpikeNode)
        {
            oneArg[0] = (nint)pSn;
            _methodBindPtrcall(_mbClassDbInstantiate, classDb, oneArg, (nint)retVariant);
        }

        nint spikeNode = 0;
        var variantToObject = (delegate* unmanaged<nint, nint, void>)_getVariantToTypeCtor(TYPE_OBJECT);
        variantToObject((nint)(&spikeNode), (nint)retVariant);
        _variantDestroy((nint)retVariant);
        Check(spikeNode != 0, "ClassDB.instantiate(&\"SpikeNode\") returned an object");

        // -- String-returning ptrcall: the engine sees our class identity --
        var className = ObjectGetClass(spikeNode);
        Check(className == "SpikeNode", $"Object.get_class() = \"{className}\"");

        // -- ptrcall with StringName / object / bool / enum arguments --
        var snNodeName = 0UL;
        _stringNameNewLatin1((nint)(&snNodeName), Latin1("spike_node"), 0);
        nint* oneArg2 = stackalloc nint[1];
        oneArg2[0] = (nint)(&snNodeName);
        _methodBindPtrcall(_mbNodeSetName, spikeNode, oneArg2, 0);

        byte boolTrue = TRUE;
        oneArg2[0] = (nint)(&boolTrue);
        _methodBindPtrcall(_mbNodeSetProcess, spikeNode, oneArg2, 0);

        long internalMode = 0; // Node.InternalMode.INTERNAL_MODE_DISABLED
        byte forceReadable = 0;
        nint* threeArgs = stackalloc nint[3];
        threeArgs[0] = (nint)(&spikeNode);
        threeArgs[1] = (nint)(&forceReadable);
        threeArgs[2] = (nint)(&internalMode);
        _methodBindPtrcall(_mbNodeAddChild, root, threeArgs, 0);

        byte includeInternal = 0;
        long childCount = 0;
        oneArg2[0] = (nint)(&includeInternal);
        _methodBindPtrcall(_mbNodeGetChildCount, root, oneArg2, (nint)(&childCount));
        Check(childCount >= 1, $"root.get_child_count() = {childCount} after add_child");

        // -- pump the loop via hash-free variant_call on the fork-only GodotInstance.iteration() --
        var instanceVariant = stackalloc byte[VARIANT_SIZE];
        var objectToVariant = (delegate* unmanaged<nint, nint, void>)_getVariantFromTypeCtor(TYPE_OBJECT);
        objectToVariant((nint)instanceVariant, (nint)(&instance));

        var variantToBool = (delegate* unmanaged<nint, nint, void>)_getVariantToTypeCtor(TYPE_BOOL);
        var quit = false;
        var frames = 0;
        var iterRet = stackalloc byte[VARIANT_SIZE];
        while (!quit && _processCalls < 60 && frames < 600)
        {
            CallError err;
            fixed (ulong* pIter = &_snIteration)
            {
                _variantCall((nint)instanceVariant, (nint)pIter, null, 0, (nint)iterRet, &err);
            }
            if (err.error != 0)
            {
                Console.Error.WriteLine($"[spike] variant_call(iteration) failed: error={err.error}");
                break;
            }
            byte quitByte = 0;
            variantToBool((nint)(&quitByte), (nint)iterRet);
            _variantDestroy((nint)iterRet);
            quit = quitByte != 0;
            frames++;
        }
        _variantDestroy((nint)instanceVariant);

        Check(_readyCalled, "_ready() virtual dispatched into C#");
        Check(_processCalls >= 60, $"_process() virtual dispatched {_processCalls} times (last delta = {_lastDelta:F4}s)");

        UtilityPrint($"[spike] engine-side goodbye after {frames} iterations");

        // -- teardown: the tree owns spike_node; destroying the instance must fire free_instance --
        _destroyGodotInstance(instance);
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

    // ---------------------------------------------------------------- init --

    [UnmanagedCallersOnly]
    private static byte InitCallback(nint getProcAddress, nint library, GDExtensionInitialization* init)
    {
        _getProcAddress = (delegate* unmanaged<nint, nint>)getProcAddress;
        _library = library;

        _stringNameNewLatin1 = (delegate* unmanaged<nint, nint, byte, void>)Proc("string_name_new_with_latin1_chars");
        _stringNewUtf8 = (delegate* unmanaged<nint, nint, void>)Proc("string_new_with_utf8_chars");
        _stringToUtf8Chars = (delegate* unmanaged<nint, nint, long, long>)Proc("string_to_utf8_chars");
        _getVariantFromTypeCtor = (delegate* unmanaged<int, nint>)Proc("get_variant_from_type_constructor");
        _getVariantToTypeCtor = (delegate* unmanaged<int, nint>)Proc("get_variant_to_type_constructor");
        _variantDestroy = (delegate* unmanaged<nint, void>)Proc("variant_destroy");
        _variantCall = (delegate* unmanaged<nint, nint, nint*, long, nint, CallError*, void>)Proc("variant_call");
        _variantGetPtrUtilityFunction = (delegate* unmanaged<nint, long, nint>)Proc("variant_get_ptr_utility_function");
        _variantGetPtrDestructor = (delegate* unmanaged<int, nint>)Proc("variant_get_ptr_destructor");
        _globalGetSingleton = (delegate* unmanaged<nint, nint>)Proc("global_get_singleton");
        _classdbGetMethodBind = (delegate* unmanaged<nint, nint, long, nint>)Proc("classdb_get_method_bind");
        _methodBindPtrcall = (delegate* unmanaged<nint, nint, nint*, nint, void>)Proc("object_method_bind_ptrcall");
        _classdbConstructObject3 = (delegate* unmanaged<nint, nint>)Proc("classdb_construct_object3");
        _objectSetInstance = (delegate* unmanaged<nint, nint, nint, void>)Proc("object_set_instance");
        _classdbRegisterClass6 = (delegate* unmanaged<nint, nint, nint, nint, void>)Proc("classdb_register_extension_class6");
        _objectGetInstanceId = (delegate* unmanaged<nint, ulong>)Proc("object_get_instance_id");

        // Persistent StringNames (is_static=1: never unref'd, safe across teardown).
        fixed (ulong* p = &_snSpikeNode) _stringNameNewLatin1((nint)p, Latin1("SpikeNode"), 1);
        fixed (ulong* p = &_snNode) _stringNameNewLatin1((nint)p, Latin1("Node"), 1);
        fixed (ulong* p = &_snProcess) _stringNameNewLatin1((nint)p, Latin1("_process"), 1);
        fixed (ulong* p = &_snReady) _stringNameNewLatin1((nint)p, Latin1("_ready"), 1);
        fixed (ulong* p = &_snIteration) _stringNameNewLatin1((nint)p, Latin1("iteration"), 1);

        // Method binds for CORE-registered classes (hashes from official
        // 4.7-stable extension_api.json). Scene classes (Node, SceneTree)
        // are not in ClassDB yet at this point - see InitializeLevel.
        _mbGodotInstanceStart = MethodBind("GodotInstance", "start", 2240911060);
        _mbEngineGetMainLoop = MethodBind("Engine", "get_main_loop", 1016888095);
        _mbEngineGetFps = MethodBind("Engine", "get_frames_per_second", 1740695150);
        _mbClassDbInstantiate = MethodBind("ClassDB", "instantiate", 2760726917);
        _mbObjectGetClass = MethodBind("Object", "get_class", 201670096);

        _utilPrint = UtilityFunction("print", 2648703342);

        init->minimum_initialization_level = 0; // CORE, same as twodog.engine
        init->initialize = (nint)(delegate* unmanaged<nint, int, void>)&InitializeLevel;
        init->deinitialize = (nint)(delegate* unmanaged<nint, int, void>)&DeinitializeLevel;
        return TRUE;
    }

    [UnmanagedCallersOnly]
    private static void InitializeLevel(nint userdata, int level)
    {
        if (level != LEVEL_SCENE) return;

        // Scene classes exist in ClassDB only from SCENE level onward.
        _mbSceneTreeGetRoot = MethodBind("SceneTree", "get_root", 1757182445);
        _mbNodeAddChild = MethodBind("Node", "add_child", 3863233950);
        _mbNodeSetName = MethodBind("Node", "set_name", 3304788590);
        _mbNodeGetChildCount = MethodBind("Node", "get_child_count", 894402480);
        _mbNodeSetProcess = MethodBind("Node", "set_process", 2586408642);

        RegisterSpikeNode();
        _sceneLevelInitialized = true;
    }

    [UnmanagedCallersOnly]
    private static void DeinitializeLevel(nint userdata, int level)
    {
    }

    private static void RegisterSpikeNode()
    {
        var info = new ClassCreationInfo6
        {
            is_exposed = TRUE,
            create_instance_func = (nint)(delegate* unmanaged<nint, byte, nint>)&CreateInstance,
            free_instance_func = (nint)(delegate* unmanaged<nint, nint, void>)&FreeInstance,
            get_virtual_call_data_func = (nint)(delegate* unmanaged<nint, nint, uint, nint>)&GetVirtualCallData,
            call_virtual_with_data_func = (nint)(delegate* unmanaged<nint, nint, nint, nint*, nint, void>)&CallVirtualWithData,
        };

        fixed (ulong* pClass = &_snSpikeNode)
        fixed (ulong* pParent = &_snNode)
        {
            _classdbRegisterClass6(_library, (nint)pClass, (nint)pParent, (nint)(&info));
        }
    }

    // ------------------------------------------------- extension callbacks --

    [UnmanagedCallersOnly]
    private static nint CreateInstance(nint classUserdata, byte notifyPostinitialize)
    {
        nint obj;
        fixed (ulong* pNode = &_snNode) obj = _classdbConstructObject3((nint)pNode);
        fixed (ulong* pClass = &_snSpikeNode) _objectSetInstance(obj, (nint)pClass, INSTANCE_TOKEN);
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
        if (payload == _snProcess) return 1;
        if (payload == _snReady) return 2;
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

    private static nint Proc(string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name + '\0');
        fixed (byte* p = bytes)
        {
            var fn = _getProcAddress((nint)p);
            if (fn == 0) throw new EntryPointNotFoundException($"GDExtension proc not found: {name}");
            return fn;
        }
    }

    private static nint MethodBind(string cls, string method, long hash)
    {
        var snClass = 0UL;
        var snMethod = 0UL;
        _stringNameNewLatin1((nint)(&snClass), Latin1(cls), 0);
        _stringNameNewLatin1((nint)(&snMethod), Latin1(method), 0);
        var mb = _classdbGetMethodBind((nint)(&snClass), (nint)(&snMethod), hash);
        if (mb == 0) Console.Error.WriteLine($"[spike] WARNING: no method bind for {cls}.{method} (hash {hash})");
        return mb;
    }

    private static nint UtilityFunction(string name, long hash)
    {
        var sn = 0UL;
        _stringNameNewLatin1((nint)(&sn), Latin1(name), 0);
        return _variantGetPtrUtilityFunction((nint)(&sn), hash);
    }

    private static void UtilityPrint(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message + '\0');
        ulong str = 0;
        fixed (byte* p = bytes) _stringNewUtf8((nint)(&str), (nint)p);

        var variant = stackalloc byte[VARIANT_SIZE];
        var stringToVariant = (delegate* unmanaged<nint, nint, void>)_getVariantFromTypeCtor(TYPE_STRING);
        stringToVariant((nint)variant, (nint)(&str));

        nint* args = stackalloc nint[1];
        args[0] = (nint)variant;
        ((delegate* unmanaged<nint, nint*, int, void>)_utilPrint)(0, args, 1);

        _variantDestroy((nint)variant);
        var stringDtor = (delegate* unmanaged<nint, void>)_variantGetPtrDestructor(TYPE_STRING);
        stringDtor((nint)(&str));
    }

    private static string ObjectGetClass(nint obj)
    {
        ulong str = 0;
        _methodBindPtrcall(_mbObjectGetClass, obj, null, (nint)(&str));
        var len = _stringToUtf8Chars((nint)(&str), 0, 0);
        var buffer = new byte[len];
        fixed (byte* p = buffer) _stringToUtf8Chars((nint)(&str), (nint)p, len);
        var stringDtor = (delegate* unmanaged<nint, void>)_variantGetPtrDestructor(TYPE_STRING);
        stringDtor((nint)(&str));
        return Encoding.UTF8.GetString(buffer);
    }

    // Scratch StringName slots for one-shot singleton lookups from Main.
    private static ulong _tmpSn, _tmpSn2;

    private static nint Sn(ref ulong slot, string name)
    {
        fixed (ulong* p = &slot)
        {
            if (slot == 0) _stringNameNewLatin1((nint)p, Latin1(name), 1);
            return (nint)p;
        }
    }

    private static readonly List<nint> _latin1Pins = [];

    /// <summary>Null-terminated Latin-1 bytes, pinned for the process lifetime.</summary>
    private static nint Latin1(string s)
    {
        var ptr = Marshal.StringToCoTaskMemAnsi(s);
        _latin1Pins.Add(ptr);
        return ptr;
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
