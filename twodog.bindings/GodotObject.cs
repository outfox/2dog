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
    private nint _nativePtr;

    /// <summary>Raw engine object pointer; 0 once released or freed.</summary>
    public nint NativePtr { get => _nativePtr; internal set => _nativePtr = value; }

    public ulong InstanceId { get; internal set; }

    public bool IsRefCounted { get; }

    internal GodotObject(nint nativePtr, bool isRefCounted)
    {
        NativePtr = nativePtr;
        IsRefCounted = isRefCounted;
        // Generated public ctors pass 0 and let AttachNew construct the native
        // side (needed so registered user classes construct as their own
        // extension class, resolved from the most-derived runtime type).
        InstanceId = nativePtr != 0 ? GdExtensionInterface.ObjectGetInstanceId(nativePtr) : 0;
    }

    /// <summary>
    /// Engine-virtual dispatch chain. Generated classes override this with
    /// checks for their own declared virtuals (interned StringName payload
    /// comparison) and fall through to base. Called only for virtuals the
    /// registry reported as overridden.
    /// </summary>
    internal virtual bool __CallVirtual(ulong nameSn, nint* args, nint ret) => false;

    /// <summary>True while the engine object exists (ObjectID-validated, never dangles).</summary>
    public bool IsValid => NativePtr != 0 && GdExtensionInterface.ObjectGetInstanceFromId(InstanceId) != 0;

    /// <summary>Called from the binding free callback when the engine destroyed the object.</summary>
    internal void NativeFreed() => NativePtr = 0;

    /// <summary>
    /// Awaits a one-shot signal emission, GodotSharp-style:
    /// <c>await ToSignal(GetTree().CreateTimer(1.0), "timeout")</c>.
    /// The result is the emitted signal's arguments.
    /// </summary>
    public SignalAwaiter ToSignal(GodotObject source, StringName signal) => new(source, signal);

    /// <summary>Explicitly destroys a non-RefCounted object (Node.free() semantics).</summary>
    public void Free()
    {
        if (IsRefCounted) throw new InvalidOperationException("RefCounted objects die by unreferencing, not Free().");
        var ptr = Interlocked.Exchange(ref _nativePtr, 0);
        if (ptr == 0) return;
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
        if (!IsRefCounted) return;
        // Atomic claim: concurrent Dispose/finalizer must enqueue exactly one
        // unref (a double-unref would underflow the engine refcount).
        var ptr = Interlocked.Exchange(ref _nativePtr, 0);
        if (ptr == 0) return;
        // Never touch the engine from the finalizer thread: defer to the
        // host's per-frame drain.
        DisposalQueue.EnqueueUnref(ptr);
    }
}
