using twodog;
using twodog.fixture;

namespace twodog.tests.EngineTests;

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

        // Same guard the fixtures apply: pre-load game assemblies into the
        // Default load context before the first engine start. The game's main
        // scene instantiates a C# script, and if this collection happens to
        // run before the fixture-based ones, skipping this would let Godot
        // load the game assembly into its PluginLoadContext, causing type
        // identity mismatches for later collections.
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        var engine = new Engine("restart-direct", projectDir, "--headless");
        engine.Start();
        try
        {
            Assert.False(engine.Iteration());

            // Only one instance may run at a time. Disposing the failed
            // Engine must not affect the running instance.
            using var concurrent = new Engine("restart-concurrent", projectDir, "--headless");
            Assert.Throws<InvalidOperationException>(() => concurrent.Start());

            Assert.False(engine.Iteration());
        }
        finally
        {
            engine.Dispose();
        }

        // Sequential restart: after disposing, a new engine can be started
        // in the same process.
        using var engine2 = new Engine("restart-direct-2", projectDir, "--headless");
        engine2.Start();
        Assert.False(engine2.Iteration());
    }

    [Fact]
    public async Task Quit_DestroysCompletesAndAllowsRestart()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        using var engine = new Engine("managed-lifecycle", projectDir, "--headless");
        engine.Start();
        var exited = 0;
        engine.Exited += () => exited++;

        engine.RequestQuit();
        engine.RequestQuit();
        Assert.True(engine.Iteration());
        Assert.False(engine.Completion.IsCompleted);
        engine.Dispose();
        await engine.Completion;
        Assert.Equal(1, exited);
        Assert.Throws<InvalidOperationException>(() => engine.Iteration());
        Assert.Throws<InvalidOperationException>(() => engine.Start());

        using var restarted = new Engine("managed-lifecycle-2", projectDir, "--headless");
        restarted.Start();
        Assert.False(restarted.Iteration());
    }

    [Fact]
    public void LifecycleStateGuardsRejectInvalidUseAndStaleEngine()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        using var unstarted = new Engine("unstarted", projectDir, "--headless");
        Assert.Throws<InvalidOperationException>(() => unstarted.Iteration());
        Assert.Throws<InvalidOperationException>(() => unstarted.RequestQuit());

        var stale = new Engine("stale", projectDir, "--headless");
        stale.Start();
        stale.Dispose();

        using var current = new Engine("current", projectDir, "--headless");
        current.Start();
        Assert.Throws<InvalidOperationException>(() => stale.Iteration());
        Assert.Throws<InvalidOperationException>(() => stale.Start());
        Assert.False(current.Iteration());
    }

    [Fact]
    public async Task Dispose_ContainsExitedHandlerFailuresAndNotifiesEveryHandler()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        var engine = new Engine("exit-handlers", projectDir, "--headless");
        engine.Start();
        var notified = false;
        engine.Exited += () => throw new InvalidOperationException("expected handler failure");
        engine.Exited += () => notified = true;

        engine.Dispose();

        await engine.Completion;
        Assert.True(notified);
    }

    [Fact]
    public async Task Dispose_WithoutStartCompletesWithoutExited()
    {
        var engine = new Engine("never-started", Engine.ResolveProjectDir(), "--headless");
        var exited = false;
        engine.Exited += () => exited = true;

        engine.Dispose();

        await engine.Completion;
        Assert.False(exited);
    }

    [Fact]
    public void CommandLineQuitStateDoesNotLeakIntoRestart()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        using (var first = new Engine("quit-after", projectDir, "--headless", "--quit-after", "1"))
        {
            first.Start();
            Assert.True(first.Iteration());
        }

        using var second = new Engine("after-quit-after", projectDir, "--headless");
        second.Start();
        Assert.False(second.Iteration());
    }

    [Fact]
    public void OutputSettingsDoNotLeakIntoRestart()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        using (var first = new Engine("quiet-lifetime", projectDir, "--headless", "--quiet"))
        {
            first.Start();
            Assert.False(Godot.Engine.PrintToStdOut);
            Godot.Engine.PrintErrorMessages = false;
        }

        using var second = new Engine("normal-output-lifetime", projectDir, "--headless");
        second.Start();
        Assert.True(Godot.Engine.PrintToStdOut);
        Assert.True(Godot.Engine.PrintErrorMessages);
        Assert.False(second.Iteration());
    }

    [Fact]
    public void NativeInstanceRejectsSecondStartWithoutBreakingLifetime()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        using var engine = new Engine("double-native-start", projectDir, "--headless");
        var instance = engine.Start();

        Assert.False(instance.Start());
        Assert.False(engine.Iteration());
    }

    [Fact]
    public void FailedSetupDoesNotPoisonNextLifetime()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        using (var failed = new Engine("failed-setup", projectDir, "--rendering-driver"))
            Assert.ThrowsAny<Exception>(() => failed.Start());

        using var recovered = new Engine("after-failed-setup", projectDir, "--headless");
        recovered.Start();
        Assert.False(recovered.Iteration());
    }

    [Fact]
    public void InvalidDisplayDriverDoesNotPoisonNextLifetime()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        using (var failed = new Engine(
                   "failed-second-phase", projectDir, "--display-driver", "definitely-missing"))
            Assert.ThrowsAny<Exception>(() => failed.Start());

        using var recovered = new Engine("after-failed-second-phase", projectDir, "--headless");
        recovered.Start();
        Assert.False(recovered.Iteration());
    }

    [Fact]
    public void FailedMainLoopStartDoesNotPoisonNextLifetime()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        // Node exists, but is not a MainLoop. This reaches Main::start() after
        // both setup phases, without invoking a platform error dialog.
        using (var failed = new Engine("failed-main-loop", projectDir, "--headless", "--main-loop", "Node"))
            Assert.ThrowsAny<Exception>(() => failed.Start());

        using var recovered = new Engine("after-failed-main-loop", projectDir, "--headless");
        recovered.Start();
        Assert.False(recovered.Iteration());
    }
}
