using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

/// <summary>
/// ToSignal awaiters: the awaiter contract (pending await, already-completed
/// await, argument capture), the engine-driven resume path (SceneTreeTimer),
/// and connection/lifetime behavior. Continuations run inline in the engine's
/// signal callback (main thread), so completion is deterministic in tests.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class SignalAwaiterTests(GodotBindingsFixture godot)
{
    private static void EnsureRegistered() => ClassRegistry.Register<ExportNode>();

    [Fact]
    public async Task Await_PendingSignal_ResumesOnEmit()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            var task = AwaitDied(node);
            Assert.False(task.IsCompleted); // parked at the await

            node.EmitSignalDied();          // fires inline on this (engine) thread
            Assert.True(task.IsCompleted);  // continuation ran synchronously
            Assert.True(await task);
        }
        finally
        {
            node.Free();
        }
    }

    private static async Task<bool> AwaitDied(ExportNode node)
    {
        await node.ToSignal(node, "died");
        return true;
    }

    [Fact]
    public async Task Await_CapturesSignalArguments()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            var task = AwaitScored(node);
            node.EmitSignalScored(99, "awaiter");
            Assert.True(task.IsCompleted);

            var args = await task;
            Assert.Equal(2, args.Length);
            Assert.Equal(99, args[0].AsInt64());
            Assert.Equal("awaiter", args[1].AsString());
            foreach (var a in args) a.Dispose();
        }
        finally
        {
            node.Free();
        }
    }

    private static async Task<Variant[]> AwaitScored(ExportNode node) =>
        await node.ToSignal(node, "scored");

    [Fact]
    public void Await_AlreadyCompleted_ContinuesSynchronously()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            // Emit BETWEEN creating the awaiter and awaiting it.
            var awaiter = node.ToSignal(node, "died");
            node.EmitSignalDied();
            Assert.True(awaiter.IsCompleted);

            var task = Consume(awaiter);
            Assert.True(task.IsCompleted); // await on a completed awaiter runs through
        }
        finally
        {
            node.Free();
        }
    }

    private static async Task Consume(SignalAwaiter awaiter) => await awaiter;

    [Fact]
    public void Await_SceneTreeTimer_ResumesViaFramePump()
    {
        var tree = (SceneTree)Godot.Engine.GetMainLoop()!;
        var task = AwaitTimer(tree);

        var frames = 0;
        while (!task.IsCompleted && frames++ < 600)
        {
            godot.PumpFrames(1);
        }
        Assert.True(task.IsCompleted, $"timer await did not resume within {frames} frames");
    }

    private static async Task AwaitTimer(SceneTree tree)
    {
        var timer = tree.CreateTimer(0.02, true, false, false)!;
        await tree.ToSignal(timer, "timeout");
        timer.Dispose();
    }

    [Fact]
    public void Await_OneShot_DisconnectsAfterFire()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            var task = AwaitDied(node);
            node.EmitSignalDied();
            Assert.True(task.IsCompleted);

            // One-shot: a second emission must not fault anything (the
            // connection is gone; the awaiter is finished).
            node.EmitSignalDied();
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void ToSignal_UnknownSignal_Throws()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            using var mute = new EngineOutputMute();
            Assert.Throws<InvalidOperationException>(() => node.ToSignal(node, "no_such_signal"));
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void Awaiter_SurvivesGc_WhileConnectionPending()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            var task = AwaitDied(node);
            // Only the engine connection roots the awaiter now.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            node.EmitSignalDied();
            Assert.True(task.IsCompleted, "awaiter was collected while the connection was pending");
        }
        finally
        {
            node.Free();
        }
    }
}

/// <summary>The main-thread marshalling side: Task-based awaits and Post/Send.</summary>
[Collection(nameof(GodotBindingsCollection))]
public class SynchronizationContextTests
{
    [Fact]
    public void Post_FromBackgroundThread_RunsOnPumpingThread()
    {
        var context = new GodotSynchronizationContext();
        var mainThread = System.Environment.CurrentManagedThreadId;
        var ranOn = 0;

        var posted = Task.Run(
            () => context.Post(_ => ranOn = System.Environment.CurrentManagedThreadId, null),
            TestContext.Current.CancellationToken);
        SpinWait.SpinUntil(() => posted.IsCompleted);

        Assert.Equal(0, ranOn); // nothing until pumped
        context.Pump();
        Assert.Equal(mainThread, ranOn);
    }

    [Fact]
    public void Send_FromBackgroundThread_BlocksUntilPumped()
    {
        var context = new GodotSynchronizationContext();
        var ran = false;

        var sending = Task.Run(() => context.Send(_ => ran = true, null), TestContext.Current.CancellationToken);
        // Give the background Send a moment to enqueue, then pump it through.
        while (!sending.IsCompleted)
        {
            context.Pump();
        }
        Assert.True(ran);
    }

    [Fact]
    public void Send_OnMainThread_RunsInline()
    {
        var context = new GodotSynchronizationContext();
        var ran = false;
        context.Send(_ => ran = true, null);
        Assert.True(ran);
    }

    [Fact]
    public void AwaitTaskDelay_ResumesOnPumpingThread()
    {
        var previous = SynchronizationContext.Current;
        var context = new GodotSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var mainThread = System.Environment.CurrentManagedThreadId;
            var resumedOn = 0;

            var task = DelayThenRecord();
            while (!task.IsCompleted)
            {
                context.Pump();
                System.Threading.Thread.Sleep(1);
            }
            Assert.Equal(mainThread, resumedOn);

            async Task DelayThenRecord()
            {
                await Task.Delay(20); // completes on a timer thread, posts back
                resumedOn = System.Environment.CurrentManagedThreadId;
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
