using Godot;
using Godot.NativeInterop;
using Thread = System.Threading.Thread;

namespace twodog.bindings.tests;

/// <summary>
/// Multithreaded coverage for the RefCounted ownership protocol: the atomic
/// owned-ref release in GodotObject and the strong/weak flip in
/// InstanceBindings.ReferenceCallback (fires on any thread).
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class RefCountConcurrencyTests
{
    [Fact]
    public void ConcurrentDispose_ReleasesExactlyOneRef()
    {
        // A double-enqueued unref would underflow the engine refcount and
        // free the object while the drain still holds its pointer.
        for (var iter = 0; iter < 50; iter++)
        {
            var rc = new RefCounted();
            using var barrier = new Barrier(4);
            var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
            {
                barrier.SignalAndWait();
                rc.Dispose();
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            Assert.Equal(0, rc.NativePtr);
            DisposalQueue.Drain();
        }

        // Engine heap is intact: a fresh RefCounted behaves normally.
        using var probe = new RefCounted();
        Assert.Equal(1, probe.GetReferenceCount());
        DisposalQueue.Drain();
    }

    [Fact]
    public void ConcurrentRefChurn_OnSharedObject_KeepsCountConsistent()
    {
        var shared = new RefCounted();
        try
        {
            using var barrier = new Barrier(4);
            var failures = 0;
            var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    for (var i = 0; i < 500; i++)
                    {
                        // Each iteration: engine ref up (strong flip storm)
                        // then back down via variant destruction.
                        using var v = Variant.From(shared);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref failures);
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            Assert.Equal(0, failures);
            Assert.Equal(1, shared.GetReferenceCount()); // only the owned ref remains
            Assert.True(shared.IsValid);
        }
        finally
        {
            shared.Dispose();
            DisposalQueue.Drain();
        }
    }

    [Fact]
    public void ConcurrentCreateAndDrop_ThreadLocalObjects_Survive()
    {
        using var barrier = new Barrier(4);
        var failures = 0;
        var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
        {
            try
            {
                barrier.SignalAndWait();
                for (var i = 0; i < 200; i++)
                {
                    var rc = new RefCounted();
                    using (var v = Variant.From(rc))
                    {
                        // ref 1 -> 2 -> 1: exercises both flip directions.
                    }
                    rc.Dispose();
                }
            }
            catch
            {
                Interlocked.Increment(ref failures);
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Equal(0, failures);
        DisposalQueue.Drain();
    }
}
