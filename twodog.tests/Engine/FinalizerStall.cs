using System.Runtime.CompilerServices;

namespace twodog.tests.EngineTests;

/// <summary>
/// Holds the finalizer thread until released, or until the tracked wrappers were disposed: engine shutdown waits
/// for pending finalizers after disposing, which must not deadlock on this stall.
/// </summary>
internal sealed class FinalizerStall
{
    private readonly ManualResetEventSlim _entered = new();
    private readonly ManualResetEventSlim _released = new();
    private WeakReference<Godot.GodotObject>[]? _awaitedDisposal;

    public static FinalizerStall Begin()
    {
        var stall = new FinalizerStall();
        Abandon(stall);
        GC.Collect();
        Assert.True(stall._entered.Wait(TimeSpan.FromSeconds(10)), "The finalizer thread never ran the stall.");
        return stall;
    }

    public void ReleaseOnceDisposed(WeakReference<Godot.GodotObject>[] wrappers) =>
        Volatile.Write(ref _awaitedDisposal, wrappers);

    private bool AwaitedDisposed() => Volatile.Read(ref _awaitedDisposal) is { } wrappers &&
        wrappers.All(w => !w.TryGetTarget(out var wrapper) || wrapper.NativeInstance == IntPtr.Zero);

    public void Release() => _released.Set();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Abandon(FinalizerStall stall) => _ = new Blocker(stall);

    private sealed class Blocker(FinalizerStall stall)
    {
        ~Blocker()
        {
            stall._entered.Set();
            var deadline = System.Environment.TickCount64 + 30_000;
            while (!stall._released.Wait(10) && !stall.AwaitedDisposed() &&
                   System.Environment.TickCount64 < deadline)
            {
            }
        }
    }
}
