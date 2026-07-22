using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Godot.NativeInterop;

/// <summary>
/// Managed-wrapper identity for engine-owned objects, built on GDExtension
/// instance bindings. One binding slot per (object, our library token).
///
/// Lifetime protocol for RefCounted objects:
/// - The wrapper owns exactly ONE engine reference (adopted from transferred
///   return values, or explicitly taken for borrowed pointers). The engine
///   object therefore cannot die while its wrapper is alive; death is always
///   initiated by the managed side (finalizer -> DisposalQueue -> unref).
/// - The GCHandle in the binding flips strength on the 1&lt;-&gt;2 refcount edge,
///   driven by the engine's reference callback:
///     refcount &gt; 1  -&gt; STRONG (engine holds refs and may call back in;
///                       managed state must survive without C# roots)
///     refcount == 1 -&gt; WEAK   (only the wrapper's own ref remains; liveness
///                       is now decided purely by the .NET GC)
/// Non-RefCounted objects always get WEAK handles (stateless proxies for now);
/// the engine can free them at any time and wrappers re-materialize on demand.
/// </summary>
public static unsafe class InstanceBindings
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BindingSlot
    {
        public nint ObjectPtr;
        public nint Handle;   // GCHandle (strong or weak) on the wrapper
        public int Strong;    // 1 = strong
    }

    private static readonly object Gate = new();
    private static readonly nint Callbacks = AllocCallbacks();

    /// <summary>Bindings freed by the engine so far (diagnostics).</summary>
    public static long FreedBindings { get; private set; }

    private static nint AllocCallbacks()
    {
        var p = (GDExtensionInstanceBindingCallbacks*)NativeMemory.AllocZeroed((nuint)sizeof(GDExtensionInstanceBindingCallbacks));
        p->create_callback = (nint)(delegate* unmanaged<nint, nint, nint>)&CreateCallback;
        p->free_callback = (nint)(delegate* unmanaged<nint, nint, nint, void>)&FreeCallback;
        p->reference_callback = (nint)(delegate* unmanaged<nint, nint, byte, byte>)&ReferenceCallback;
        return (nint)p;
    }

    /// <summary>Constructs a bare engine object of the given class (no binding yet).</summary>
    public static nint ConstructRaw(string godotClass)
    {
        var sn = StringNames.Get(godotClass).Opaque;
        return GdExtensionInterface.ClassdbConstructObject3((nint)(&sn));
    }

    public static nint GetSingletonPtr(string singletonName)
    {
        var sn = StringNames.Get(singletonName).Opaque;
        return GdExtensionInterface.GlobalGetSingleton((nint)(&sn));
    }

    /// <summary>
    /// Binds a wrapper the managed side just constructed (generated public
    /// ctors) to its freshly created engine object. For RefCounted classes the
    /// wrapper takes first ownership via init_ref.
    /// </summary>
    public static void Attach(GodotObject wrapper)
    {
        if (wrapper.NativePtr == 0) throw new InvalidOperationException($"Engine refused to construct {wrapper.GetType().Name} (editor-only class in a template build?).");
        lock (Gate)
        {
            var slot = (BindingSlot*)GdExtensionInterface.ObjectGetInstanceBinding(wrapper.NativePtr, GdExtensionHost.Library, Callbacks);
            GCHandle.FromIntPtr(slot->Handle).Free();
            slot->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(wrapper, GCHandleType.Weak));
            slot->Strong = 0;
            // No init_ref here: classdb_construct_object3 already established
            // refcount=1 owned by the caller (ClassDB::_instantiate_internal
            // with p_with_refcount=true) - the wrapper adopts that reference.
            if (wrapper.IsRefCounted) RecomputeStrength(slot);
        }
    }

    /// <summary>
    /// Installs a weak binding on an extension instance so GetOrCreate resolves
    /// the same managed object. The strong owner is the GDExtension instance
    /// handle (ClassRegistry), not the binding.
    /// </summary>
    internal static void InstallWeakBinding(GodotObject wrapper)
    {
        lock (Gate)
        {
            var slot = (BindingSlot*)GdExtensionInterface.ObjectGetInstanceBinding(wrapper.NativePtr, GdExtensionHost.Library, Callbacks);
            GCHandle.FromIntPtr(slot->Handle).Free();
            slot->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(wrapper, GCHandleType.Weak));
            slot->Strong = 0;
        }
    }

    private static nint _mbGetClass;

    /// <summary>Reads the object's actual class name (Object.get_class ptrcall).</summary>
    public static string ReadClassName(nint objectPtr)
    {
        if (_mbGetClass == 0) _mbGetClass = MethodBinds.Resolve("Object", "get_class", 201670096);
        ulong str = 0;
        GdExtensionInterface.ObjectMethodBindPtrcall(_mbGetClass, objectPtr, 0, (nint)(&str));
        return NativeString.ReadAndDestroy(ref str);
    }

    /// <summary>
    /// Registry-driven wrap: resolves the object's actual class so the wrapper
    /// comes out as the most-derived generated type. Fork-only classes not in
    /// extension_api.json fall back to a plain non-refcounted GodotObject.
    /// </summary>
    public static GodotObject? GetOrCreate(nint objectPtr, bool adoptRef)
    {
        if (objectPtr == 0) return null;
        var className = ReadClassName(objectPtr);
        return ApiRegistry.Entries.TryGetValue(className, out var entry)
            ? GetOrCreate(objectPtr, entry.RefCounted, adoptRef, entry.Factory)
            : GetOrCreate(objectPtr, refCounted: false, adoptRef: false);
    }

    /// <summary>
    /// Returns the managed wrapper for an engine object, creating one if needed.
    /// <paramref name="adoptRef"/> is true when the caller received a transferred
    /// reference (ptrcall object returns): a new wrapper adopts it as its owned
    /// ref; an existing wrapper releases the surplus.
    /// </summary>
    public static GodotObject? GetOrCreate(nint objectPtr, bool refCounted, bool adoptRef, Func<nint, bool, GodotObject>? factory = null)
    {
        if (objectPtr == 0) return null;

        lock (Gate)
        {
            var slot = (BindingSlot*)GdExtensionInterface.ObjectGetInstanceBinding(objectPtr, GdExtensionHost.Library, Callbacks);

            if (GCHandle.FromIntPtr(slot->Handle).Target is GodotObject { NativePtr: not 0 } alive)
            {
                // Existing live wrapper. It already owns its reference, so a
                // transferred one is surplus - give it back. (Never the last
                // ref: the wrapper still owns one, so no death path here.)
                if (refCounted && adoptRef) RefCountedNative.Unreference(objectPtr);
                return alive;
            }

            // No wrapper, or a collected/disposed husk whose owned ref is (or
            // will be) released via the DisposalQueue - the replacement needs
            // its own reference.
            //
            // Order matters: install the handle in the slot BEFORE taking the
            // reference. Reference() re-enters ReferenceCallback on this thread
            // (Gate is reentrant) and may reflip the slot's handle; a stale
            // local GCHandle from before that call must never be touched again.
            var wrapper = factory?.Invoke(objectPtr, refCounted) ?? new GodotObject(objectPtr, refCounted);
            GCHandle.FromIntPtr(slot->Handle).Free();
            slot->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(wrapper, GCHandleType.Weak));
            slot->Strong = 0;

            if (refCounted && !adoptRef) RefCountedNative.Reference(objectPtr);
            if (refCounted) RecomputeStrength(slot);
            return wrapper;
        }
    }

    private static void RecomputeStrength(BindingSlot* slot)
    {
        var strong = RefCountedNative.GetReferenceCount(slot->ObjectPtr) > 1;
        if (strong != (slot->Strong == 1))
            Reflip(slot, GCHandle.FromIntPtr(slot->Handle), strong);
    }

    /// <summary>Current handle strength for an object's binding (diagnostics; null if no binding).</summary>
    public static bool? DebugIsStrong(nint objectPtr)
    {
        lock (Gate)
        {
            var slot = (BindingSlot*)GdExtensionInterface.ObjectGetInstanceBinding(objectPtr, GdExtensionHost.Library, Callbacks);
            return slot == null ? null : slot->Strong == 1;
        }
    }

    // ------------------------------------------------------- callbacks --

    [UnmanagedCallersOnly]
    private static nint CreateCallback(nint token, nint instance)
    {
        try
        {
            // Reached only via our own ObjectGetInstanceBinding calls (under Gate).
            // Allocates the slot with a weak handle to no target; GetOrCreate fills
            // in the wrapper right after.
            var slot = (BindingSlot*)NativeMemory.AllocZeroed((nuint)sizeof(BindingSlot));
            slot->ObjectPtr = instance;
            slot->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(null, GCHandleType.Weak));
            slot->Strong = 0;
            return (nint)slot;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog.bindings: unhandled exception in binding create callback: {e}");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void FreeCallback(nint token, nint instance, nint binding)
    {
        try
        {
            // Engine object died with a binding attached.
            var slot = (BindingSlot*)binding;
            lock (Gate)
            {
                var handle = GCHandle.FromIntPtr(slot->Handle);
                if (handle.Target is GodotObject wrapper) wrapper.NativeFreed();
                handle.Free();
                NativeMemory.Free(slot);
                FreedBindings++;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog.bindings: unhandled exception in binding free callback: {e}");
        }
    }

    [UnmanagedCallersOnly]
    private static byte ReferenceCallback(nint token, nint binding, byte isReference)
    {
        // Fires on every RefCounted::reference/unreference, any thread.
        var slot = (BindingSlot*)binding;
        try
        {
            lock (Gate)
            {
                var count = RefCountedNative.GetReferenceCount(slot->ObjectPtr);
                var handle = GCHandle.FromIntPtr(slot->Handle);

                if (isReference != 0)
                {
                    if (count > 1 && slot->Strong == 0) Reflip(slot, handle, strong: true);
                    return 1;
                }

                if (count == 1 && slot->Strong == 1) Reflip(slot, handle, strong: false);

                // Return value gates death at count==0: block it only while a live,
                // still-attached wrapper exists (with the owned-ref protocol that
                // implies count >= 1, so this is a belt-and-braces guard). A
                // disposed husk (NativePtr == 0) that is still rooted managed-side
                // must NOT block death - its owned ref was already released.
                return handle.Target is GodotObject { NativePtr: not 0 } ? (byte)0 : (byte)1;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog.bindings: unhandled exception in binding reference callback: {e}");
            // Wrapper state is unknown here: block death (0) - a leak beats a
            // use-after-free.
            return 0;
        }
    }

    private static void Reflip(BindingSlot* slot, GCHandle old, bool strong)
    {
        var target = old.Target;
        var fresh = GCHandle.Alloc(target, strong ? GCHandleType.Normal : GCHandleType.Weak);
        old.Free();
        slot->Handle = GCHandle.ToIntPtr(fresh);
        slot->Strong = strong ? 1 : 0;
    }
}
