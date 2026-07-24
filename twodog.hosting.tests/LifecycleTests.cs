using System.Collections.Concurrent;
using System.Runtime.Loader;
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

/// <summary>Reports whether the xunit assert assembly resolved to the default
/// ALC (1, shared identity) or into this instance's own ALC (0, isolated).</summary>
public sealed class SharedIdentityProbeProgram : IEngineProgram
{
    public int Run(IInstanceContext ctx)
    {
        ctx.SignalBooted();
        var alc = AssemblyLoadContext.GetLoadContext(typeof(Assert).Assembly);
        return alc == AssemblyLoadContext.Default ? 1 : 0;
    }
}

/// <summary>Holds the process-wide boot gate: runs (i.e. has acquired the gate),
/// posts a marker, then blocks without ever signaling boot until released.</summary>
public sealed class StuckBootProgram : IEngineProgram
{
    public int Run(IInstanceContext ctx)
    {
        ctx.Post("in-gate");
        (ctx.State as SemaphoreSlim)?.Wait(TimeSpan.FromMinutes(2));
        return 0;
    }
}

public sealed class LifecycleTests
{
    private static string TempProject => Path.GetTempPath();

    [Fact]
    public async Task FailingProgramFaultsBothCompletionAndBooted()
    {
        HostGuard.SkipUnlessSupported();
        using var host = new EngineHost();
        var instance = host.Start<FailingProgram>(new() { Tag = "lc-fail", ProjectDir = TempProject });
        await Assert.ThrowsAsync<InvalidOperationException>(() => instance.Completion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => instance.Booted);
    }

    [Fact]
    public async Task ProgramExitingWithoutBootFaultsBootedButCompletes()
    {
        HostGuard.SkipUnlessSupported();
        using var host = new EngineHost();
        var instance = host.Start<NoBootProgram>(new() { Tag = "lc-noboot", ProjectDir = TempProject });
        Assert.Equal(7, await instance.Completion);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => instance.Booted);
        Assert.Contains("without signaling boot", e.Message);
    }

    [Fact]
    public void StartAfterDisposeThrows()
    {
        HostGuard.SkipUnlessSupported();
        var host = new EngineHost();
        host.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            host.Start<NoBootProgram>(new() { Tag = "lc-disposed", ProjectDir = TempProject }));
    }

    [Fact]
    public async Task HangingProgramMakesDisposeThrowTimeoutThenCleansUpWhenReleased()
    {
        HostGuard.SkipUnlessSupported();
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
        HostGuard.SkipUnlessSupported();
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
        HostGuard.SkipUnlessSupported();
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

    [Fact]
    public async Task GodotFreeSharedAssemblyIsAcceptedAndKeepsOneIdentityAcrossAlcs()
    {
        HostGuard.SkipUnlessSupported();
        // The xunit assert assembly is Godot-free and in the test deps.json, so
        // it both passes validation and demonstrates the identity contract.
        var sharedName = typeof(Assert).Assembly.GetName().Name!;
        using var host = new EngineHost();
        var shared = host.Start<SharedIdentityProbeProgram>(new()
            { Tag = "lc-shared-ok", ProjectDir = TempProject, SharedAssemblies = [sharedName] });
        var isolated = host.Start<SharedIdentityProbeProgram>(new()
            { Tag = "lc-isolated", ProjectDir = TempProject });
        Assert.Equal(1, await shared.Completion);   // shared: default-ALC identity
        Assert.Equal(0, await isolated.Completion); // default: per-instance copy
    }

    [Fact]
    public void UnresolvableSharedRootIsRejected()
    {
        HostGuard.SkipUnlessSupported();
        using var host = new EngineHost();
        var e = Assert.Throws<ArgumentException>(() => host.Start<NoBootProgram>(new()
        {
            Tag = "lc-shared-unresolvable",
            ProjectDir = TempProject,
            SharedAssemblies = ["no.such.assembly.2dog"],
        }));
        Assert.Contains("cannot be located", e.Message);
    }

    [Fact]
    public void MissingProgramAssemblyPathThrows()
    {
        HostGuard.SkipUnlessSupported();
        using var host = new EngineHost();
        var e = Assert.Throws<ArgumentException>(() => host.Start(new InstanceOptions
            { Tag = "lc-val-noasm", ProjectDir = TempProject, ProgramTypeName = "Some.Type" }));
        Assert.Contains(nameof(InstanceOptions.ProgramAssemblyPath), e.Message);
    }

    [Fact]
    public void MissingProgramTypeNameThrows()
    {
        HostGuard.SkipUnlessSupported();
        using var host = new EngineHost();
        var e = Assert.Throws<ArgumentException>(() => host.Start(new InstanceOptions
        {
            Tag = "lc-val-notype",
            ProjectDir = TempProject,
            ProgramAssemblyPath = typeof(LifecycleTests).Assembly.Location,
        }));
        Assert.Contains(nameof(InstanceOptions.ProgramTypeName), e.Message);
    }

    [Fact]
    public void NonexistentProgramAssemblyThrows()
    {
        HostGuard.SkipUnlessSupported();
        using var host = new EngineHost();
        Assert.Throws<FileNotFoundException>(() => host.Start(new InstanceOptions
        {
            Tag = "lc-val-ghost",
            ProjectDir = TempProject,
            ProgramAssemblyPath = Path.Combine(Path.GetTempPath(), "2dog-no-such-assembly.dll"),
            ProgramTypeName = "Some.Type",
        }));
    }

    [Fact]
    public async Task BootGateTimeoutFailsClosedWhenAPriorBootIsStuck()
    {
        HostGuard.SkipUnlessSupported();
        using var release = new SemaphoreSlim(0);
        var inGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new EngineHost();
        var stuck = host.Start<StuckBootProgram>(new()
        {
            Tag = "lc-gate-stuck",
            ProjectDir = TempProject,
            State = release,
            OnMessage = _ => inGate.TrySetResult(),
        });
        // The marker only posts once the program runs, i.e. holds the gate.
        await inGate.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var blocked = host.Start<NoBootProgram>(new()
            { Tag = "lc-gate-blocked", ProjectDir = TempProject, BootGateTimeout = TimeSpan.FromMilliseconds(250) });
        var e = await Assert.ThrowsAsync<TimeoutException>(() => blocked.Completion);
        Assert.Contains("boot gate", e.Message);

        // Unstick promptly: the gate is process-wide and stalls sibling tests.
        release.Release();
        Assert.Equal(0, await stuck.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
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

    [Fact]
    public async Task ConcurrentSubmitTakeCancelCloseKeepsEveryItemAccountedFor()
    {
        var queue = new EngineWorkQueue();
        const int producers = 4, perProducer = 250;
        var items = new ConcurrentBag<EngineWorkItem>();
        var taken = new ConcurrentBag<EngineWorkItem>();
        using var stop = new CancellationTokenSource();
        var consumer = Task.Run(() =>
        {
            while (!stop.Token.IsCancellationRequested)
            {
                while (queue.TryTake(out var item))
                {
                    taken.Add(item);
                    item.Complete("ok");
                }
                Thread.SpinWait(50);
            }
        }, TestContext.Current.CancellationToken);
        await Task.WhenAll(Enumerable.Range(0, producers).Select(p => Task.Run(() =>
        {
            for (var i = 0; i < perProducer; i++)
            {
                var item = queue.Submit($"T{p}.{i}, test");
                items.Add(item);
                if (i % 8 == 0) item.TryCancel(); // races the consumer's TryTake
            }
        })));
        queue.Close(new OperationCanceledException("closed"));
        stop.Cancel();
        await consumer;

        // Every item must end in exactly one terminal state, and the running
        // state machine must hold: canceled items were never handed out, items
        // the consumer completed were handed out exactly once, everything else
        // was failed by Close.
        Assert.Equal(producers * perProducer, items.Count);
        var takenSet = taken.ToHashSet();
        Assert.Equal(taken.Count, takenSet.Count);
        foreach (var item in items)
        {
            Assert.True(item.Task.IsCompleted);
            if (item.Task.IsCanceled)
                Assert.DoesNotContain(item, takenSet);
            else if (item.Task.Status == TaskStatus.RanToCompletion)
                Assert.Contains(item, takenSet);
            else
                Assert.IsType<OperationCanceledException>(item.Task.Exception!.InnerException);
        }
    }
}
