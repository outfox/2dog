using twodog;

namespace twodog.tests;

// Direct Engine lifecycle tests. This collection has NO fixture on purpose:
// it manages the engine itself. Like all Godot collections it disables
// parallelization, so it runs while no fixture-owned engine instance exists.
[CollectionDefinition(nameof(EngineRestartCollection), DisableParallelization = true)]
public class EngineRestartCollection;

[Collection(nameof(EngineRestartCollection))]
public class EngineRestartTests
{
    [Fact]
    public void Start_WhileRunningThrows_AndSequentialRestartWorks()
    {
        var projectDir = Engine.ResolveProjectDir();

        var engine = new Engine("restart-direct", projectDir, "--headless");
        var godot = engine.Start();
        try
        {
            Assert.False(godot.Iteration());

            // Only one instance may run at a time. Disposing the failed
            // Engine must not affect the running instance.
            using var concurrent = new Engine("restart-concurrent", projectDir, "--headless");
            Assert.Throws<InvalidOperationException>(() => concurrent.Start());

            Assert.False(godot.Iteration());
        }
        finally
        {
            godot.Dispose();
            engine.Dispose();
        }

        // Sequential restart: after disposing, a new engine can be started
        // in the same process.
        using var engine2 = new Engine("restart-direct-2", projectDir, "--headless");
        using var godot2 = engine2.Start();
        Assert.False(godot2.Iteration());
    }
}
