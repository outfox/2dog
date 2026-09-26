using System.Runtime.CompilerServices;
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
    public async Task Completion_FollowsExitedHandlers_AndReentrantDisposalNotifiesOnce()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);
        using var engine = new Engine("exit-order", projectDir, "--headless");
        var hostContext = SynchronizationContext.Current;
        engine.Start();
        var notifications = 0;
        var completedInsideHandler = false;
        SynchronizationContext? exitContext = null;
        Task? reentrantDisposal = null;
        engine.Exited += () =>
        {
            notifications++;
            completedInsideHandler = engine.Completion.IsCompleted;
            exitContext = SynchronizationContext.Current;
            reentrantDisposal = engine.DisposeAsync().AsTask();
        };
        engine.Exited += () => notifications++;

        engine.Dispose();

        Assert.Same(hostContext, SynchronizationContext.Current);
        Assert.Same(hostContext, exitContext);
        Assert.False(completedInsideHandler);
        Assert.Equal(2, notifications);
        Assert.True(engine.Completion.IsCompletedSuccessfully);
        Assert.NotNull(reentrantDisposal);
        await reentrantDisposal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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
    public void WrappersCollectedBeforeShutdownAreReleasedByTheirOwnEngine()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        var first = new Engine("pending-finalizers", projectDir, "--headless") { CaptureErrors = true };
        FinalizerStall stall;
        try
        {
            first.Start();
            stall = FinalizerStall.Begin();
            var dropped = DropWrappers();
            // Their finalizers queue behind the stall, as on a runtime that cannot run finalizers during shutdown.
            GC.Collect();
            stall.ReleaseOnceDisposed(dropped);
        }
        finally
        {
            first.Dispose();
        }

        using var second = new Engine("after-pending-finalizers", projectDir, "--headless") { CaptureErrors = true };
        second.Start();
        // Whatever the first shutdown left pending now runs while the second engine owns the memory.
        stall.Release();
        GC.WaitForPendingFinalizers();
        Assert.False(second.Iteration());

        Assert.Empty(first.Errors.Drain());
        Assert.Empty(second.Errors.Drain());
    }

    [Fact]
    public void ClassSettingsAreRegisteredForEveryLifetime()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        // Classes register once per process; settings defined while registering them must return with every engine.
        string[] classSettings =
        [
            "editor/naming/node_name_num_separator", "editor/naming/node_name_casing",
            "gui/timers/button_shortcut_feedback_highlight_time", "gui/common/default_scroll_deadzone",
            "gui/timers/text_edit_idle_detect_sec", "gui/common/text_edit_undo_stack_max_size",
            "editor/movie_writer/mix_rate",
        ];
        var lifetimes = new List<string[]>();
        for (var lifetime = 0; lifetime < 2; lifetime++)
        {
            using var engine = new Engine($"settings-{lifetime}", projectDir, "--headless");
            engine.Start();
            foreach (var setting in classSettings)
                Assert.True(Godot.ProjectSettings.HasSetting(setting), $"Lifetime {lifetime} lacks {setting}.");
            lifetimes.Add([.. Godot.ProjectSettings.Singleton.GetPropertyList().Select(p => (string)p["name"]).Order()]);
        }

        Assert.Equal(lifetimes[0], lifetimes[1]);
    }

    [Fact]
    public void IndexedPropertiesWorkInEveryLifetime()
    {
        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        // These classes list indexed properties ("point_0/position", "item_0/text") through helpers they register once
        // per process.
        string[] classes =
        [
            "Curve", "Curve2D", "Curve3D", "LabelSettings", "AudioStreamRandomizer", "ItemList", "PopupMenu",
            "OptionButton", "MenuButton", "TabBar", "TabContainer", "FileDialog", "TileMap",
        ];
        for (var lifetime = 0; lifetime < 2; lifetime++)
        {
            using var engine = new Engine($"indexed-{lifetime}", projectDir, "--headless") { CaptureErrors = true };
            engine.Start();
            foreach (var name in classes)
            {
                var instance = Godot.ClassDB.Instantiate(name).AsGodotObject();
                Assert.NotEmpty(instance.GetPropertyList());
                if (instance is Godot.Node node) node.Free();
                else instance.Dispose();
            }

            using var curve = new Godot.Curve();
            curve.AddPoint(new Godot.Vector2(0, 0));
            curve.AddPoint(new Godot.Vector2(1, 1));
            using var copy = (Godot.Curve)curve.Duplicate();
            Assert.Equal(new Godot.Vector2(1, 1), copy.GetPointPosition(1));
            Assert.Empty(engine.Errors.Drain());
        }
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

        // Keep AppKit out of worker-thread tests, including failed startup.
        // The malformed option must remain last so its argument is still missing.
        using (var failed = new Engine("failed-setup", projectDir, "--headless", "--rendering-driver"))
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

        // Select the headless macOS OS before setup validates the invalid driver.
        using (var failed = new Engine(
                   "failed-second-phase", projectDir, "--headless", "--display-driver", "definitely-missing"))
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

    /// <summary>
    /// Drops wrappers whose native objects only they keep alive; the returned references observe them without doing so.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Godot.GodotObject>[] DropWrappers()
    {
        var dropped = new WeakReference<Godot.GodotObject>[8];
        for (var i = 0; i < dropped.Length; i++)
            dropped[i] = new WeakReference<Godot.GodotObject>(new Godot.RefCounted(), trackResurrection: true);
        _ = new Godot.Collections.Array { new Godot.RefCounted() };
        return dropped;
    }

    /// <summary>
    /// Holds the finalizer thread until released, or until the tracked wrappers were disposed: engine shutdown waits
    /// for pending finalizers after disposing, which must not deadlock on this stall.
    /// </summary>
    private sealed class FinalizerStall
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
}
