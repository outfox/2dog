using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Godot.NativeInterop;

/// <summary>
/// Registers user-defined C# classes as real ClassDB extension classes.
///
/// Model (phase 3a): a registered instance's managed object IS the extension
/// instance - the GDExtension instance pointer is a strong GCHandle on it,
/// freed by the engine's free_instance callback, so managed lifetime equals
/// native lifetime. Two creation paths converge in AttachNew:
///  - C#-first: `new TestNode()` - the generated base ctor calls AttachNew,
///    which sees the runtime type is registered and constructs the native
///    base class + object_set_instance.
///  - engine-first: ClassDB.instantiate / scene load - CreateInstance
///    constructs the native base, parks it in a thread-static pending slot,
///    and instantiates the managed type; its ctor chain lands in AttachNew,
///    which adopts the pending native instead of constructing a new one.
///
/// Virtual dispatch: get_virtual_call_data returns non-null only for virtuals
/// the user actually overrides (reflection against the generated stubs,
/// cached); call_virtual_with_data routes into the generated __CallVirtual
/// chain, which decodes ptrcall args and invokes the C# override.
/// </summary>
public static unsafe class ClassRegistry
{
    // Registered types are reached by reflection (Activator.CreateInstance,
    // __BindMembers, virtual-override probing) - keep them whole when trimming.
    private const DynamicallyAccessedMemberTypes RegisteredTypeMembers =
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods;

    public sealed class ClassInfo
    {
        [DynamicallyAccessedMembers(RegisteredTypeMembers)]
        public required Type Type { get; init; }
        public required string ClassName { get; init; }
        public required string ParentName { get; init; }
        public required string BaseEngineClass { get; init; }
        public required bool RefCounted { get; init; }
        internal GCHandle SelfHandle;
        internal readonly Dictionary<ulong, bool> OverriddenCache = [];
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<Type, ClassInfo> ByType = [];
    private static readonly Dictionary<ulong, ClassInfo> ByClassSn = [];

    [ThreadStatic] private static nint _pendingNative;

    /// <summary>Instances created by the engine so far (diagnostics).</summary>
    public static long EngineCreatedInstances { get; private set; }

    public static bool IsRegistered(Type type) { lock (Gate) return ByType.ContainsKey(type); }

    internal static bool TryGet(Type type, out ClassInfo info)
    {
        lock (Gate) return ByType.TryGetValue(type, out info!);
    }

    private static readonly List<Action> Pending = [];
    private static bool _flushHooked;

    /// <summary>
    /// Registers <typeparamref name="T"/> as an extension class named after the
    /// C# type, parented to its base type's class. Idempotent per type.
    /// May be called before the engine starts: registration is queued and
    /// flushed automatically at SCENE-level initialization (when engine base
    /// classes exist in ClassDB).
    /// </summary>
    public static void Register<[DynamicallyAccessedMembers(RegisteredTypeMembers)] T>() where T : GodotObject, new()
    {
        var type = typeof(T);
        lock (Gate)
        {
            if (!GdExtensionHost.SceneLevelInitialized)
            {
                if (!_flushHooked)
                {
                    _flushHooked = true;
                    GdExtensionHost.LevelInitialized += level =>
                    {
                        if (level != GDExtensionInitializationLevel.GDEXTENSION_INITIALIZATION_SCENE) return;
                        List<Action> pending;
                        lock (Gate)
                        {
                            pending = [.. Pending];
                            Pending.Clear();
                        }
                        foreach (var register in pending) register();
                    };
                }
                Pending.Add(Register<T>);
                return;
            }

            if (ByType.ContainsKey(type)) return;

            var (parentName, baseEngineClass, refCounted) = ResolveParent(type);

            var info = new ClassInfo
            {
                Type = type,
                ClassName = type.Name,
                ParentName = parentName,
                BaseEngineClass = baseEngineClass,
                RefCounted = refCounted,
            };
            info.SelfHandle = GCHandle.Alloc(info);

            var creation = new GDExtensionClassCreationInfo6
            {
                is_exposed = 1,
                create_instance_func = (nint)(delegate* unmanaged<nint, byte, nint>)&CreateInstance,
                free_instance_func = (nint)(delegate* unmanaged<nint, nint, void>)&FreeInstance,
                get_virtual_call_data_func = (nint)(delegate* unmanaged<nint, nint, uint, nint>)&GetVirtualCallData,
                call_virtual_with_data_func = (nint)(delegate* unmanaged<nint, nint, nint, nint*, nint, void>)&CallVirtualWithData,
                class_userdata = GCHandle.ToIntPtr(info.SelfHandle),
            };

            var snClass = StringNames.Get(info.ClassName).Opaque;
            var snParent = StringNames.Get(info.ParentName).Opaque;
            GdExtensionInterface.ClassdbRegisterExtensionClass6(
                GdExtensionHost.Library, (nint)(&snClass), (nint)(&snParent), (nint)(&creation));

            ByType[type] = info;
            ByClassSn[snClass] = info;

            // Source-generated member registration ([Export]/[Signal]): the
            // generator emits a static __BindMembers(MemberRegistry) into the
            // user's partial class. Declared-only: each class in a user
            // hierarchy registers its own members.
            var bind = type.GetMethod("__BindMembers",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly,
                [typeof(MemberRegistry)]);
            bind?.Invoke(null, [new MemberRegistry(info)]);
        }
    }

    /// <summary>
    /// Walks the C# base chain to the nearest generated engine class. The
    /// direct parent may itself be a registered user class.
    /// </summary>
    private static (string parentName, string baseEngineClass, bool refCounted) ResolveParent(Type type)
    {
        var bindingsAssembly = typeof(GodotObject).Assembly;
        string? parentName = null;

        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.Assembly == bindingsAssembly)
            {
                var engineName = t == typeof(GodotObject) ? "Object" : t.Name;
                parentName ??= engineName;
                var refCounted = ApiRegistry.Entries.TryGetValue(engineName, out var entry) && entry.RefCounted;
                return (parentName, engineName, refCounted);
            }
            // Intermediate user class: must be registered so the engine knows
            // the full chain - silently reparenting to the engine base would
            // hide the user hierarchy from ClassDB.
            if (!ByType.TryGetValue(t, out var parentInfo))
            {
                throw new InvalidOperationException(
                    $"{type.Name}: base class {t.Name} is not registered. " +
                    $"Register base classes first (ClassRegistry.Register<{t.Name}>()).");
            }
            parentName ??= parentInfo.ClassName;
        }
        throw new InvalidOperationException($"{type.Name} does not inherit from a Godot engine class.");
    }

    // ---------------------------------------------------- creation paths --

    /// <summary>
    /// Called from generated public ctors with `this` fully typed. Routes to
    /// the extension-instance path for registered types, or the plain wrapper
    /// path for direct engine-class construction.
    /// </summary>
    internal static void AttachNew(GodotObject instance, string declaredGdClass)
    {
        if (TryGet(instance.GetType(), out var info))
        {
            var native = _pendingNative;
            _pendingNative = 0;
            var fromEngine = native != 0;
            if (!fromEngine) native = InstanceBindings.ConstructRaw(info.BaseEngineClass);
            if (native == 0) throw new InvalidOperationException($"Engine refused to construct {info.BaseEngineClass}.");

            instance.NativePtr = native;
            instance.InstanceId = GdExtensionInterface.ObjectGetInstanceId(native);

            // Instance handle lifetime model:
            // - Non-refcounted (Node...): the engine object owns the managed
            //   instance outright - STRONG handle, freed by free_instance.
            // - RefCounted: the handle must stay WEAK (its pointer is stored
            //   engine-side and can never be swapped); the strong root while
            //   the engine holds refs comes from the instance-binding slot's
            //   strong/weak flip (RefCounted::reference fires the binding
            //   callbacks), and the managed side owns exactly one engine ref
            //   (released via finalizer -> DisposalQueue like wrappers).
            var handle = GCHandle.Alloc(instance, info.RefCounted ? GCHandleType.Weak : GCHandleType.Normal);
            var snClass = StringNames.Get(info.ClassName).Opaque;
            GdExtensionInterface.ObjectSetInstance(native, (nint)(&snClass), GCHandle.ToIntPtr(handle));

            // Install the instance binding so GetOrCreate resolves this same
            // managed object (and, for RefCounted, so its flip provides the
            // strong root when refcount > 1).
            InstanceBindings.InstallWeakBinding(instance);

            if (info.RefCounted)
            {
                // C#-first: adopt construct3's established refcount=1 as the
                // managed instance's owned ref. Engine-first: that ref belongs
                // to the engine caller (create_instance3 contract: "return
                // with refcount=1"), so take the managed instance's own on top.
                if (fromEngine) RefCountedNative.Reference(native);
            }
        }
        else
        {
            instance.NativePtr = InstanceBindings.ConstructRaw(declaredGdClass);
            instance.InstanceId = instance.NativePtr != 0 ? GdExtensionInterface.ObjectGetInstanceId(instance.NativePtr) : 0;
            InstanceBindings.Attach(instance);
        }
    }

    [UnmanagedCallersOnly]
    private static nint CreateInstance(nint classUserdata, byte notifyPostinitialize)
    {
        var info = (ClassInfo)GCHandle.FromIntPtr(classUserdata).Target!;
        var native = InstanceBindings.ConstructRaw(info.BaseEngineClass);
        if (native == 0) return 0;

        _pendingNative = native;
        try
        {
            // Ctor chain lands in AttachNew, which adopts the pending native.
            var instance = (GodotObject)Activator.CreateInstance(info.Type)!;
            EngineCreatedInstances++;
            return instance.NativePtr;
        }
        finally
        {
            _pendingNative = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void FreeInstance(nint classUserdata, nint instance)
    {
        var handle = GCHandle.FromIntPtr(instance);
        (handle.Target as GodotObject)?.NativeFreed();
        handle.Free();
    }

    // -------------------------------------------------- virtual dispatch --

    [UnmanagedCallersOnly]
    private static nint GetVirtualCallData(nint classUserdata, nint name, uint hash)
    {
        var info = (ClassInfo)GCHandle.FromIntPtr(classUserdata).Target!;
        var payload = StringNames.ReadPayload(name);
        return IsOverridden(info, payload) ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static void CallVirtualWithData(nint instance, nint name, nint userdata, nint* args, nint ret)
    {
        if (GCHandle.FromIntPtr(instance).Target is GodotObject obj)
        {
            obj.__CallVirtual(StringNames.ReadPayload(name), args, ret);
        }
    }

    private static bool IsOverridden(ClassInfo info, ulong nameSnPayload)
    {
        lock (Gate)
        {
            if (info.OverriddenCache.TryGetValue(nameSnPayload, out var cached)) return cached;

            var overridden = false;
            var gdName = StringNames.Read(nameSnPayload);
            if (GeneratedVirtualNames.Map.TryGetValue(gdName, out var csName))
            {
                var mi = info.Type.GetMethod(csName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                // Overridden iff the most-derived declaration lives outside the
                // bindings assembly (i.e. is not the generated no-op stub).
                overridden = mi is not null && mi.DeclaringType!.Assembly != typeof(GodotObject).Assembly;
            }
            info.OverriddenCache[nameSnPayload] = overridden;
            return overridden;
        }
    }
}
