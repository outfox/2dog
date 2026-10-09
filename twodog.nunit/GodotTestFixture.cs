using System;
using System.Threading;
using NUnit.Framework;

namespace twodog.Testing.NUnit;

/// <summary>A headless engine shared by a sequential NUnit fixture on its owner thread.</summary>
[SingleThreaded]
[NonParallelizable]
[FixtureLifeCycle(LifeCycle.SingleInstance)]
public abstract class GodotTestFixture
{
    private FixtureBase? _godot;

    /// <summary>The engine fixture, available from derived setup through derived teardown.</summary>
    protected FixtureBase Godot => _godot ?? throw new InvalidOperationException("Godot has not started.");

    /// <summary>Override to configure a headless engine. NUnit runners do not own macOS's process main thread.</summary>
    protected virtual FixtureBase CreateFixture() => new HeadlessFixture();

    /// <summary>Whether unconsumed native errors and warnings fail tests. Prefer Godot.Errors.Expect.</summary>
    protected virtual bool FailOnGodotErrors => true;

    /// <summary>Starts the engine on the thread NUnit uses for this fixture.</summary>
    [OneTimeSetUp]
    public void StartGodot()
    {
        var context = SynchronizationContext.Current;
        try { _godot = CreateFixture(); }
        finally
        {
            // Godot installs its own context at startup. NUnit must retain its own context so its
            // SingleThreaded async adapter can pump test/setup/teardown continuations on this thread.
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    /// <summary>Fails the next test on unconsumed startup errors or deferred errors from an earlier test.</summary>
    [SetUp]
    public void CheckGodotBeforeTest() => CheckErrors("before this test started (startup or deferred work)");

    /// <summary>Checks errors after derived teardown has cleaned up native objects.</summary>
    [TearDown]
    public void CheckGodotAfterTest() => CheckErrors("during this test");

    private void CheckErrors(string when)
    {
        if (!FailOnGodotErrors) return;
        var errors = Godot.Errors.Drain();
        if (errors.Length > 0)
            throw new AssertionException($"Godot reported {errors.Length} error(s) {when}:\n{string.Join("\n", errors)}");
    }

    /// <summary>Disposes the engine on its owner thread, after all derived one-time teardown methods.</summary>
    [OneTimeTearDown]
    public void StopGodot()
    {
        if (_godot is not { } godot) return;
        try { CheckErrors("during fixture setup or teardown"); }
        finally
        {
            try { godot.Dispose(); }
            finally { _godot = null; }
        }
    }
}
