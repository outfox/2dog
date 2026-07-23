using twodog.Hosting.Xunit;

namespace twodog.Hosting.Tests;

// Failure-path programs: none of these boot an engine, so they are cheap and
// do not consume boot time - they exercise the host lifecycle contract only.

public sealed class FailingProgram : IEngineProgram
{
    public int Run(IInstanceContext ctx) => throw new InvalidOperationException("boom");
}

public sealed class NoBootProgram : IEngineProgram
{
    public int Run(IInstanceContext ctx) => 7;
}

public sealed class HangingProgram : IEngineProgram
{
    public int Run(IInstanceContext ctx)
    {
        // Signal first: holding the boot gate would stall every other test's
        // boot for the gate timeout. This program tests SHUTDOWN behavior only.
        ctx.SignalBooted();
        // Deliberately ignores QuitRequested until the test releases it, so
        // the hang is bounded and cleanup after release is also verified.
        (ctx.State as SemaphoreSlim)?.Wait(TimeSpan.FromMinutes(2));
        return 0;
    }
}

public sealed class PostingProgram : IEngineProgram
{
    public int Run(IInstanceContext ctx)
    {
        ctx.SignalBooted();
        ctx.Post("booted");
        while (!ctx.QuitRequested) Thread.Sleep(10);
        return 0;
    }
}

public sealed class LifecycleTests
{
    private static string TempProject => Path.GetTempPath();

    [Fact]
    public async Task FailingProgramFaultsBothCompletionAndBooted()
    {
        using var host = new EngineHost();
        var instance = host.Start<FailingProgram>(new() { Tag = "lc-fail", ProjectDir = TempProject });
        await Assert.ThrowsAsync<InvalidOperationException>(() => instance.Completion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => instance.Booted);
    }

    [Fact]
    public async Task ProgramExitingWithoutBootFaultsBootedButCompletes()
    {
        using var host = new EngineHost();
        var instance = host.Start<NoBootProgram>(new() { Tag = "lc-noboot", ProjectDir = TempProject });
        Assert.Equal(7, await instance.Completion);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => instance.Booted);
        Assert.Contains("without signaling boot", e.Message);
    }

    [Fact]
    public void StartAfterDisposeThrows()
    {
        var host = new EngineHost();
        host.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            host.Start<NoBootProgram>(new() { Tag = "lc-disposed", ProjectDir = TempProject }));
    }

    [Fact]
    public async Task HangingProgramMakesDisposeThrowTimeoutThenCleansUpWhenReleased()
    {
        using var release = new SemaphoreSlim(0);
        var host = new EngineHost();
        var instance = host.Start<HangingProgram>(new()
            { Tag = "lc-hang", ProjectDir = TempProject, State = release, ShutdownTimeout = TimeSpan.FromSeconds(1) });
        await instance.Booted.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Throws<TimeoutException>(instance.Dispose);
        // Releasing the blocker lets the program exit - no thread outlives the test.
        release.Release();
        Assert.Equal(0, await instance.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        host.Dispose(); // clean now: no aggregate timeout
    }

    [Fact]
    public async Task DisposeFromOnMessageCallbackDoesNotDeadlock()
    {
        using var host = new EngineHost();
        EngineInstance? instance = null;
        var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instance = host.Start<PostingProgram>(new()
        {
            Tag = "lc-post",
            ProjectDir = TempProject,
            OnMessage = _ =>
            {
                // Runs on the engine thread: Dispose must degrade to RequestQuit
                // instead of waiting for its own thread.
                instance!.Dispose();
                posted.TrySetResult();
            },
        });
        await posted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(0, await instance.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SharedAssembliesReferencingBindingsAreRejected()
    {
        using var host = new EngineHost();
        // The test assembly itself references the bindings stack transitively.
        var e = Assert.Throws<ArgumentException>(() => host.Start<NoBootProgram>(new()
        {
            Tag = "lc-shared",
            ProjectDir = TempProject,
            SharedAssemblies = ["twodog.hosting.tests"],
        }));
        Assert.Contains("per-instance", e.Message);
    }
}

public sealed class WorkQueueTests
{
    [Fact]
    public async Task CloseFailsPendingAndRejectsNewWork()
    {
        var queue = new EngineWorkQueue();
        var pending = queue.Submit("Some.Type, some.assembly");
        queue.Close(new OperationCanceledException("shut down"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.Task);
        var late = queue.Submit("Other.Type, some.assembly");
        await Assert.ThrowsAsync<OperationCanceledException>(() => late.Task);
        Assert.False(queue.TryTake(out _));
    }

    [Fact]
    public async Task CanceledItemsAreNeverHandedToTheConsumer()
    {
        var queue = new EngineWorkQueue();
        var item = queue.Submit("Some.Type, some.assembly");
        Assert.True(item.TryCancel());
        await Assert.ThrowsAsync<TaskCanceledException>(() => item.Task);
        Assert.False(queue.TryTake(out _));
    }

    [Fact]
    public async Task RunningItemsCannotBeCanceled()
    {
        var queue = new EngineWorkQueue();
        _ = queue.Submit("Some.Type, some.assembly");
        Assert.True(queue.TryTake(out var taken));
        Assert.False(taken!.TryCancel());
        taken.Complete("done");
        Assert.Equal("done", await taken.Task);
    }
}
