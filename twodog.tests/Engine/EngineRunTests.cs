using twodog;
using twodog.fixture;

namespace twodog.tests;

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

        using var engine = new Engine("run-loop", projectDir, "--headless");
        using var godot = engine.Start();

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
                engine.Tree.Quit();
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
}
