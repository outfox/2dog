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

    /// <summary>
    /// Returns the managed wrapper for an engine object, creating one if needed.
    /// <paramref name="adoptRef"/> is true when the caller received a transferred
    /// reference (ptrcall object returns): a new wrapper adopts it as its owned
    /// ref; an existing wrapper releases the surplus.
    /// </summary>
    public static GodotObject? GetOrCreate(nint objectPtr, bool refCounted, bool adoptRef)
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
            var wrapper = new GodotObject(objectPtr, refCounted);
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
        // Reached only via our own ObjectGetInstanceBinding calls (under Gate).
        // Allocates the slot with a weak handle to no target; GetOrCreate fills
        // in the wrapper right after.
        var slot = (BindingSlot*)NativeMemory.AllocZeroed((nuint)sizeof(BindingSlot));
        slot->ObjectPtr = instance;
        slot->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(null, GCHandleType.Weak));
        slot->Strong = 0;
        return (nint)slot;
    }

    [UnmanagedCallersOnly]
    private static void FreeCallback(nint token, nint instance, nint binding)
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

    [UnmanagedCallersOnly]
    private static byte ReferenceCallback(nint token, nint binding, byte isReference)
    {
        // Fires on every RefCounted::reference/unreference, any thread.
        var slot = (BindingSlot*)binding;
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

            // Return value gates death at count==0: allow it only when no live
            // managed wrapper remains (with the owned-ref protocol, a live
            // wrapper implies count >= 1, so this is a belt-and-braces guard).
            return handle.Target is null ? (byte)1 : (byte)0;
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
