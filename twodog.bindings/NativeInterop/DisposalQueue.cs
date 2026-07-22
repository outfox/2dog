using System.Collections.Concurrent;

namespace Godot.NativeInterop;

/// <summary>
/// Deferred release of engine references owned by collected managed wrappers.
///
/// Finalizers must not call into the engine (Godot teardown and refcount-death
/// paths are not finalizer-thread-safe; GodotSharp's history of shutdown
/// crashes comes from exactly this). Instead, finalizers enqueue here and the
/// host drains at known-safe points: once per iteration and before destroying
/// the Godot instance. 2dog owns the main loop, so those points are guaranteed.
/// </summary>
public static unsafe class DisposalQueue
{
    private static readonly ConcurrentQueue<nint> PendingUnrefs = new();

    /// <summary>Queues the release of one owned reference on a RefCounted object.</summary>
    public static void EnqueueUnref(nint refCounted) => PendingUnrefs.Enqueue(refCounted);

    /// <summary>Total objects released so far (diagnostics).</summary>
    public static long Released { get; private set; }

    /// <summary>
    /// Releases all pending references. Call from the main thread only.
    /// Objects whose count hits zero are destroyed (which in turn fires the
    /// instance-binding free callback).
    /// </summary>
    public static void Drain()
    {
        while (PendingUnrefs.TryDequeue(out var ptr))
        {
            if (RefCountedNative.Unreference(ptr))
            {
                GdExtensionInterface.ObjectDestroy(ptr);
            }
            Released++;
        }
    }
}
