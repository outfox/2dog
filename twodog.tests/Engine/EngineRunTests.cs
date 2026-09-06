using twodog;
using twodog.fixture;

namespace twodog.tests.EngineTests;

// Direct Engine.Run() lifecycle tests. Like EngineRestartTests this
// collection has NO fixture on purpose: Run() drives the engine to quit,
// which would poison a shared fixture instance for later tests.
[CollectionDefinition(nameof(EngineRunCollection), DisableParallelization = true)]
public class EngineRunCollection;

[Collection(nameof(EngineRunCollection))]
public class EngineRunTests
{
    [Fact]
    public void Run_InvokesPerFrameAfterEachIteration_UntilQuit()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        using var engine = new Engine("run-loop", args: ["--headless"]);
        engine.Start();

        var frames = 0;
        var framesProcessedAtFirstCallback = 0UL;

        engine.Run(perFrame: () =>
        {
            frames++;
            if (frames == 1)
            {
                // Contract: perFrame runs AFTER the engine iteration - the
                // process-frame counter must already have advanced. (The web
                // host implements the same iterate-then-perFrame order.)
                framesProcessedAtFirstCallback = Godot.Engine.GetProcessFrames();
            }

            if (frames == 3)
            {
                engine.RequestQuit();
            }
        });

        // Quit is honored on the next iteration, which must NOT invoke
        // perFrame again (matching the loop's exit-before-callback shape).
        Assert.Equal(3, frames);
        Assert.True(framesProcessedAtFirstCallback >= 1,
            $"perFrame ran before the first engine iteration (process frames: {framesProcessedAtFirstCallback})");
    }

    [Fact]
    public void Run_WithoutStart_Throws()
    {
        using var engine = new Engine("run-unstarted", Engine.ResolveProjectDir(), "--headless");
        Assert.Throws<InvalidOperationException>(() => engine.Run());
    }

    [Fact]
    public void Run_RejectsNestedPumps_AndDisposalFromPerFrameStopsTheLoop()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);
        using var engine = new Engine("nested-pump", projectDir, "--headless");
        engine.Start();
        var frames = 0;
        engine.Run(() =>
        {
            frames++;
            Assert.Throws<InvalidOperationException>(() => engine.Run());
            Assert.Throws<InvalidOperationException>(() => engine.Iteration());
            engine.Dispose();
        });
        Assert.Equal(1, frames);
        Assert.True(engine.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public void Dispose_FromNativeFrame_DefersDestructionUntilTheFrameReturns()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);
        using var engine = new Engine("dispose-in-frame", projectDir, "--headless");
        engine.Start();
        var callbackRan = false;
        var completedInsideFrame = false;
        var treeSurvived = false;
        engine.Tree.ProcessFrame += () =>
        {
            if (callbackRan) return;
            callbackRan = true;
            engine.Dispose();
            completedInsideFrame = engine.Completion.IsCompleted;
            treeSurvived = Godot.GodotObject.IsInstanceValid(engine.Tree.Root);
        };

        Assert.True(engine.Iteration());
        Assert.True(callbackRan);
        Assert.False(completedInsideFrame);
        Assert.True(treeSurvived);
        Assert.True(engine.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public void Lifecycle_RejectsForeignThreadBeforeTouchingNativeState()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);
        using var engine = new Engine("thread-affinity", projectDir, "--headless");
        engine.Start();
        Exception? iterateFailure = null;
        Exception? disposeFailure = null;
        var thread = new Thread(() =>
        {
            iterateFailure = Record.Exception(() => engine.Iteration());
            disposeFailure = Record.Exception(() => engine.Dispose());
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.IsType<InvalidOperationException>(iterateFailure);
        Assert.IsType<InvalidOperationException>(disposeFailure);
        Assert.False(engine.Iteration());
    }
}
