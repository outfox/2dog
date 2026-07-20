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
    public sealed class ClassInfo
    {
        public required Type Type { get; init; }
        public required string ClassName { get; init; }
        public required string ParentName { get; init; }
        public required string BaseEngineClass { get; init; }
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

    /// <summary>
    /// Registers <typeparamref name="T"/> as an extension class named after the
    /// C# type, parented to its base type's class. Idempotent per type.
    /// </summary>
    public static void Register<T>() where T : GodotObject, new()
    {
        var type = typeof(T);
        lock (Gate)
        {
            if (ByType.ContainsKey(type)) return;

            var (parentName, baseEngineClass, refCounted) = ResolveParent(type);
            if (refCounted)
                throw new NotSupportedException(
                    $"{type.Name}: RefCounted-based user classes are not supported yet (phase 3a covers Object/Node-based classes).");

            var info = new ClassInfo
            {
                Type = type,
                ClassName = type.Name,
                ParentName = parentName,
                BaseEngineClass = baseEngineClass,
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
            // the full chain.
            if (ByType.TryGetValue(t, out var parentInfo))
            {
                parentName ??= parentInfo.ClassName;
            }
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

            // The engine instance owns the managed object: strong handle,
            // released in FreeInstance when the engine object dies.
            var handle = GCHandle.Alloc(instance);
            var snClass = StringNames.Get(info.ClassName).Opaque;
            GdExtensionInterface.ObjectSetInstance(native, (nint)(&snClass), GCHandle.ToIntPtr(handle));

            // Also install the instance binding so GetOrCreate resolves this
            // same managed object (weak: the instance handle is the owner).
            InstanceBindings.InstallWeakBinding(instance);
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
        var payload = *(ulong*)name;
        return IsOverridden(info, payload) ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static void CallVirtualWithData(nint instance, nint name, nint userdata, nint* args, nint ret)
    {
        if (GCHandle.FromIntPtr(instance).Target is GodotObject obj)
        {
            obj.__CallVirtual(*(ulong*)name, args, ret);
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
