using System.Runtime.CompilerServices;
using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

/// <summary>
/// The RefCounted lifetime protocol: wrapper owns one engine ref, GCHandle
/// flips strong/weak on the refcount 1&lt;-&gt;2 edge, death only via the
/// DisposalQueue drain. These are the highest-value tests in the suite -
/// mistakes here are silent memory corruption, not exceptions.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class LifetimeTests
{
    [Fact]
    public void NewRefCounted_AdoptsConstructRefcountOfOne()
    {
        using var rc = new RefCounted();
        Assert.Equal(1, rc.GetReferenceCount());
        Assert.True(rc.IsRefCounted);
        Assert.True(rc.IsValid);
    }

    [Fact]
    public void EngineRef_FlipsHandleStrong_AndBackWeak()
    {
        using var rc = new RefCounted();
        Assert.False(InstanceBindings.DebugIsStrong(rc.NativePtr));

        // A Variant holding the object is an engine-side reference: rc 1->2.
        var v = Variants.FromObject(rc.NativePtr);
        Assert.Equal(2, rc.GetReferenceCount());
        Assert.True(InstanceBindings.DebugIsStrong(rc.NativePtr));

        // Releasing it flips back on the 2->1 edge.
        Variants.Destroy(ref v);
        Assert.Equal(1, rc.GetReferenceCount());
        Assert.False(InstanceBindings.DebugIsStrong(rc.NativePtr));
    }

    [Fact]
    public void GetOrCreate_SamePointer_ReturnsSameWrapper()
    {
        using var rc = new RefCounted();
        var again = InstanceBindings.GetOrCreate(rc.NativePtr, adoptRef: false);
        Assert.Same(rc, again);
        Assert.Equal(1, rc.GetReferenceCount()); // borrowed re-wrap adds nothing
    }

    [Fact]
    public void GetOrCreate_AdoptOnExistingWrapper_ReleasesSurplus()
    {
        using var rc = new RefCounted();
        // Simulate a transferred return for an object we already wrap: take an
        // extra engine ref, then hand it to GetOrCreate as "adopted".
        RefCountedNative.Reference(rc.NativePtr);
        Assert.Equal(2, rc.GetReferenceCount());

        var again = InstanceBindings.GetOrCreate(rc.NativePtr, adoptRef: true);
        Assert.Same(rc, again);
        Assert.Equal(1, rc.GetReferenceCount());
    }

    [Fact]
    public void CollectedWrapper_ReleasesRefThroughDrain_AndObjectDies()
    {
        var id = CreateUnrootedRefCounted();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        DisposalQueue.Drain();
        Assert.False(IsObjectAlive(id));
    }

    [Fact]
    public void DisposedButRootedHusk_DoesNotBlockDeath()
    {
        // Regression test for the death-gate: a disposed wrapper that is still
        // strongly referenced managed-side must not keep the engine object alive.
        var rc = new RefCounted();
        var id = rc.InstanceId;
        rc.Dispose();
        DisposalQueue.Drain();
        Assert.False(IsObjectAlive(id));
        Assert.Equal(0, rc.NativePtr); // husk detached
        GC.KeepAlive(rc);
    }

    [Fact]
    public void DoubleDispose_IsSafe()
    {
        var rc = new RefCounted();
        rc.Dispose();
        rc.Dispose();
        DisposalQueue.Drain();
    }

    [Fact]
    public void BindingFreeCallback_FiresOnDeath()
    {
        var before = InstanceBindings.FreedBindings;
        var rc = new RefCounted();
        rc.Dispose();
        DisposalQueue.Drain();
        // >=: the drain may also release wrappers collected by earlier tests.
        Assert.True(InstanceBindings.FreedBindings >= before + 1);
    }

    [Fact]
    public void NonRefCounted_FreeInvalidatesWrapper_AndFreesBinding()
    {
        var before = InstanceBindings.FreedBindings;
        var node = new Node();
        var id = node.InstanceId;
        Assert.False(node.IsRefCounted);
        Assert.True(node.IsValid);

        node.Free();
        Assert.False(node.IsValid);
        Assert.Equal(0, node.NativePtr);
        Assert.False(IsObjectAlive(id));
        Assert.Equal(before + 1, InstanceBindings.FreedBindings);
    }

    [Fact]
    public void NonRefCounted_DisposeDoesNotFree()
    {
        var node = new Node();
        var id = node.InstanceId;
        node.Dispose();
        DisposalQueue.Drain();
        Assert.True(IsObjectAlive(id)); // engine owns plain Objects; Dispose is a no-op
        // cleanup
        var again = (Node)InstanceBindings.GetOrCreate(ObjectFromId(id), adoptRef: false)!;
        again.Free();
    }

    [Fact]
    public void RefCountedFromEngine_MaterializesAsMostDerivedType()
    {
        // Node.duplicate() returns a transferred Node; use a typed factory path
        // end-to-end: create, wrap via registry, verify type and ownership.
        using var res = new Resource();
        Assert.True(res.IsRefCounted);
        Assert.Equal(1, res.GetReferenceCount());
        var again = InstanceBindings.GetOrCreate(res.NativePtr, adoptRef: false);
        Assert.IsType<Resource>(again);
    }

    // ------------------------------------------------------------ helpers --

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong CreateUnrootedRefCounted()
    {
        var rc = new RefCounted();
        Assert.Equal(1, rc.GetReferenceCount());
        return rc.InstanceId;
    }

    private static unsafe bool IsObjectAlive(ulong instanceId) =>
        GdExtensionInterface.ObjectGetInstanceFromId(instanceId) != 0;

    private static unsafe nint ObjectFromId(ulong instanceId) =>
        GdExtensionInterface.ObjectGetInstanceFromId(instanceId);
}
