using twodog.Hosting.Xunit;
using Engine = twodog.Engine;

namespace twodog.Hosting.Tests;

/// <summary>App-story program: boots its engine, waits for the shared release
/// gate (so the test can hold both instances alive simultaneously), pumps 60
/// frames, reports the count.</summary>
public sealed class CountingProgram : IEngineProgram
{
    public int Run(IInstanceContext ctx)
    {
        var engine = new Engine(ctx.Tag, ctx.ProjectDir, ctx.Args) { NativePath = ctx.NativePath };
        using var godot = engine.Start();
        ctx.SignalBooted();
        (ctx.State as ManualResetEventSlim)?.Wait(TimeSpan.FromSeconds(30));
        var frames = 0;
        for (var i = 0; i < 60; i++)
        {
            if (godot.Iteration()) break;
            frames++;
        }
        engine.Dispose();
        return frames;
    }
}

/// <summary>Pumps in LOCKSTEP with its sibling: neither engine may advance to
/// frame i+1 until both completed frame i. A regression that serializes engine
/// lifetimes (one pumping only after the other finished) times out the barrier.</summary>
public sealed class PacedProgram : IEngineProgram
{
    public const int Frames = 30;

    public int Run(IInstanceContext ctx)
    {
        var barrier = (Barrier)(ctx.State ?? throw new InvalidOperationException("PacedProgram needs a Barrier as State."));
        var engine = new Engine(ctx.Tag, ctx.ProjectDir, ctx.Args) { NativePath = ctx.NativePath };
        using var godot = engine.Start();
        ctx.SignalBooted();
        var frames = 0;
        for (var i = 0; i < Frames; i++)
        {
            if (godot.Iteration()) break;
            frames++;
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30))) return -1;
        }
        engine.Dispose();
        return frames;
    }
}

/// <summary>The "two twodog.Engine instances in one app" story, via EngineHost.</summary>
public sealed class DualStartTests
{
    [Fact]
    public async Task TwoEnginesRunConcurrentlyInOneProcess()
    {
        using var gate = new ManualResetEventSlim(false);
        using var host = new EngineHost();

        var a = host.Start<CountingProgram>(new()
            { Tag = "app-A", ProjectDir = ScratchProject.Create("app-A"), Args = ["--headless"], State = gate });
        var b = host.Start<CountingProgram>(new()
            { Tag = "app-B", ProjectDir = ScratchProject.Create("app-B"), Args = ["--headless"], State = gate });

        // Both booted => two live engines in this process right now.
        await Task.WhenAll(a.Booted, b.Booted)
            .WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);
        gate.Set();

        Assert.Equal(60, await a.Completion);
        Assert.Equal(60, await b.Completion);
        Assert.NotEqual(a.NativePath, b.NativePath);
    }

    [Fact]
    public async Task EnginesPumpFramesConcurrentlyInLockstep()
    {
        using var barrier = new Barrier(2);
        using var host = new EngineHost();
        var a = host.Start<PacedProgram>(new()
            { Tag = "paced-A", ProjectDir = ScratchProject.Create("paced-A"), Args = ["--headless"], State = barrier });
        var b = host.Start<PacedProgram>(new()
            { Tag = "paced-B", ProjectDir = ScratchProject.Create("paced-B"), Args = ["--headless"], State = barrier });
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(PacedProgram.Frames, await a.Completion.WaitAsync(TimeSpan.FromMinutes(3), token));
        Assert.Equal(PacedProgram.Frames, await b.Completion.WaitAsync(TimeSpan.FromMinutes(3), token));
    }
}
