using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit.Sdk;

namespace twodog.tests.EngineTests;

[Collection<HeadlessCollection>]
public class GodotAssertionTests(HeadlessFixture godot) : IDisposable
{
    private readonly List<Node> _nodes = [];

    private Node CreateNode()
    {
        var node = new Node();
        _nodes.Add(node);
        return node;
    }

    public void Dispose()
    {
        foreach (var node in _nodes)
            if (GodotObject.IsInstanceValid(node)) node.Free();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AwaitAsync_PumpsNativeSignalsAndPreservesTheOwnerThread()
    {
        var owner = System.Environment.CurrentManagedThreadId;
        async Task<int> NextFrame()
        {
            await godot.Tree.ToSignal(godot.Tree, SceneTree.SignalName.ProcessFrame);
            return System.Environment.CurrentManagedThreadId;
        }
        Assert.Equal(owner, await godot.AwaitAsync(NextFrame(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AwaitAsync_PropagatesTheOriginalExceptionAndCancellation()
    {
        var failure = new InvalidOperationException("operation failed");
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
            () => godot.AwaitAsync(Task.FromException(failure), cancellationToken: TestContext.Current.CancellationToken)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => godot.AwaitAsync(Task.FromCanceled(new CancellationToken(true)), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitUntilAsync_TimesOutWithTheConditionAndCanBeUsedAgain()
    {
        var ready = false;
        var failure = await Assert.ThrowsAsync<XunitException>(
            () => godot.WaitUntilAsync(() => ready, TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken));
        Assert.Contains("ready", failure.Message);
        Callable.From(() => ready = true).CallDeferred();
        await godot.WaitUntilAsync(() => ready, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitUntilAsync_RejectsNonPositiveTimeoutsAndHonorsCancellation()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => godot.WaitUntilAsync(() => false, TimeSpan.Zero, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => godot.WaitUntilAsync(() => true, cancellationToken: new CancellationToken(true)));
        using var cancellation = new CancellationTokenSource();
        Callable.From(() => cancellation.Cancel()).CallDeferred();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => godot.WaitUntilAsync(() => false, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task WaitUntilAsync_RejectsOverlappingPumps()
    {
        var ready = false;
        var waiting = godot.WaitUntilAsync(() => ready, cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => godot.WaitUntilAsync(() => true, cancellationToken: TestContext.Current.CancellationToken));
        }
        finally { ready = true; await waiting; }
    }

    [Fact]
    public void SignalExpectation_CountsSynchronousEmissionsAndChecksExactCounts()
    {
        var node = CreateNode();
        godot.Tree.Root.AddChild(node);
        using var renamed = GodotAssert.ExpectSignal(node, Node.SignalName.Renamed);
        renamed.AssertEmitted(0);
        node.Name = "First";
        node.Name = "Second";
        renamed.AssertEmitted(2);
        var failure = Assert.Throws<XunitException>(() => renamed.AssertEmitted());
        Assert.Contains("renamed", failure.Message);
        Assert.Contains("observed 2", failure.Message);
    }

    [Fact]
    public void SignalExpectation_RecordsArgumentsInOrder()
    {
        var parent = CreateNode();
        var first = CreateNode();
        var second = CreateNode();
        godot.Tree.Root.AddChild(parent);
        using var entered = GodotAssert.ExpectSignal<Node>(parent, Node.SignalName.ChildEnteredTree);
        parent.AddChild(first);
        parent.AddChild(second);
        entered.AssertEmitted(2);
        Assert.Equal(new[] { first, second }, entered.Values);
    }

    [Fact]
    public async Task SignalExpectation_WaitsForDeferredEmissions()
    {
        var node = CreateNode();
        godot.Tree.Root.AddChild(node);
        using var renamed = GodotAssert.ExpectSignal(node, Node.SignalName.Renamed);
        Callable.From(() => node.Name = "Deferred").CallDeferred();
        await renamed.WaitAsync(godot, cancellationToken: TestContext.Current.CancellationToken);
        renamed.AssertEmitted();
    }

    [Fact]
    public async Task SignalExpectation_DisconnectsOnTimeoutCancellationAndDispose()
    {
        var node = CreateNode();
        var before = node.GetSignalConnectionList(Node.SignalName.Renamed).Count;
        using (var signal = GodotAssert.ExpectSignal(node, Node.SignalName.Renamed))
        {
            Assert.Equal(before + 1, node.GetSignalConnectionList(Node.SignalName.Renamed).Count);
            await Assert.ThrowsAsync<XunitException>(() => signal.WaitAsync(godot, timeout: TimeSpan.FromMilliseconds(20), cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(before, node.GetSignalConnectionList(Node.SignalName.Renamed).Count);
        }
        using (var signal = GodotAssert.ExpectSignal(node, Node.SignalName.Renamed))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => signal.WaitAsync(godot, cancellationToken: new CancellationToken(true)));
        Assert.Equal(before, node.GetSignalConnectionList(Node.SignalName.Renamed).Count);
        using (GodotAssert.ExpectSignal(node, Node.SignalName.Renamed)) { }
        Assert.Equal(before, node.GetSignalConnectionList(Node.SignalName.Renamed).Count);
    }

    [Fact]
    public async Task SignalExpectation_FailsPromptlyIfItsSourceIsFreed()
    {
        var node = CreateNode();
        godot.Tree.Root.AddChild(node);
        using var signal = GodotAssert.ExpectSignal(node, Node.SignalName.Renamed);
        node.QueueFree();
        var failure = await Assert.ThrowsAsync<XunitException>(() => signal.WaitAsync(godot, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("source was freed", failure.Message);
    }

    [Fact]
    public void SignalExpectation_RejectsMissingSignalsAndFreedSourcesWithoutGodotErrors()
    {
        var node = CreateNode();
        using var unknown = new StringName("not_a_signal");
        Assert.Throws<ArgumentException>(() => GodotAssert.ExpectSignal(node, unknown));
        node.Free();
        Assert.Throws<ArgumentException>(() => GodotAssert.ExpectSignal(node, Node.SignalName.TreeEntered));
    }

    [Fact]
    public async Task FreedAsync_ObservesQueueFreeAndItsTreeExitSignal()
    {
        var node = CreateNode();
        godot.Tree.Root.AddChild(node);
        using var exited = GodotAssert.ExpectSignal(node, Node.SignalName.TreeExited);
        node.QueueFree();
        Assert.True(GodotObject.IsInstanceValid(node));
        await GodotAssert.FreedAsync(godot, node, cancellationToken: TestContext.Current.CancellationToken);
        exited.AssertEmitted();
        Assert.False(GodotObject.IsInstanceValid(node));
        await GodotAssert.FreedAsync(godot, node, cancellationToken: TestContext.Current.CancellationToken); // Already freed also satisfies the assertion.
    }

    [Fact]
    public async Task FreedAsync_FailsWhenTheObjectRemainsAlive()
    {
        var node = CreateNode();
        var failure = await Assert.ThrowsAsync<XunitException>(
            () => GodotAssert.FreedAsync(godot, node, TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken));
        Assert.Contains("Node to be freed", failure.Message);
        Assert.True(GodotObject.IsInstanceValid(node));
    }
}
