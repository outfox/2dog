using Godot.NativeInterop;

namespace Godot;

/// <summary>
/// Minimal managed wrapper for an engine object. Phase-1 scope: identity,
/// validity, and the RefCounted ownership protocol (the wrapper owns exactly
/// one engine reference, released via the DisposalQueue when the wrapper is
/// collected or disposed). Typed subclasses arrive with the generated API.
/// </summary>
public unsafe partial class GodotObject : IDisposable
{
    /// <summary>Raw engine object pointer; 0 once released or freed.</summary>
    public nint NativePtr { get; private set; }

    public ulong InstanceId { get; }

    public bool IsRefCounted { get; }

    internal GodotObject(nint nativePtr, bool isRefCounted)
    {
        NativePtr = nativePtr;
        IsRefCounted = isRefCounted;
        InstanceId = GdExtensionInterface.ObjectGetInstanceId(nativePtr);
    }

    /// <summary>True while the engine object exists (ObjectID-validated, never dangles).</summary>
    public bool IsValid => NativePtr != 0 && GdExtensionInterface.ObjectGetInstanceFromId(InstanceId) != 0;

    /// <summary>Called from the binding free callback when the engine destroyed the object.</summary>
    internal void NativeFreed() => NativePtr = 0;

    /// <summary>Explicitly destroys a non-RefCounted object (Node.free() semantics).</summary>
    public void Free()
    {
        if (IsRefCounted) throw new InvalidOperationException("RefCounted objects die by unreferencing, not Free().");
        var ptr = NativePtr;
        if (ptr == 0) return;
        NativePtr = 0;
        GdExtensionInterface.ObjectDestroy(ptr);
    }

    public void Dispose()
    {
        ReleaseOwnedRef();
        GC.SuppressFinalize(this);
    }

    ~GodotObject() => ReleaseOwnedRef();

    private void ReleaseOwnedRef()
    {
        var ptr = NativePtr;
        if (ptr == 0 || !IsRefCounted) return;
        NativePtr = 0;
        // Never touch the engine from the finalizer thread: defer to the
        // host's per-frame drain.
        DisposalQueue.EnqueueUnref(ptr);
    }
}
