namespace twodog.tests.HostingTests;

// Serialized: the test lowers the process-wide MaxProcessInstances, which must
// not race other collections' Start calls.
[CollectionDefinition(nameof(InstanceCapCollection), DisableParallelization = true)]
public sealed class InstanceCapCollection;

[Collection(nameof(InstanceCapCollection))]
public sealed class InstanceCapTests
{
    [Fact]
    public void StartBeyondProcessInstanceCapThrows()
    {
        HostGuard.SkipUnlessSupported();
        var original = EngineHost.MaxProcessInstances;
        EngineHost.MaxProcessInstances = 0;
        try
        {
            using var host = new EngineHost();
            var e = Assert.Throws<InvalidOperationException>(() => host.Start<NoBootProgram>(new()
            { Tag = "cap-reject", ProjectDir = Path.GetTempPath() }));
            Assert.Contains("recycling is unsupported", e.Message);
        }
        finally
        {
            // A rejected Start must not burn a slot: restoring the cap makes
            // subsequent Starts work again (verified by every later test).
            EngineHost.MaxProcessInstances = original;
        }
    }

    [Fact]
    public void StartOnDisposedHostRecyclesItsSlot()
    {
        HostGuard.SkipUnlessSupported();
        var before = EngineHost.LiveSlotCount;
        var host = new EngineHost();
        host.Dispose();
        Assert.Throws<ObjectDisposedException>(() => host.Start<NoBootProgram>(new()
        { Tag = "cap-disposed", ProjectDir = Path.GetTempPath() }));
        // The failed Start returned its slot (its pool copy was never mapped).
        Assert.Equal(before, EngineHost.LiveSlotCount);
    }
}
