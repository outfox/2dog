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
}
