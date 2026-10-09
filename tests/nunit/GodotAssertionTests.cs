using Godot;
using NUnit.Framework;
using twodog.Testing.NUnit;

namespace twodog.nunit.tests;

public class GodotAssertionTests : GodotTestFixture
{
    [Test]
    public void SignalCountsAndArgumentsAreRecordedInOrder()
    {
        var parent = new Node();
        var first = new Node();
        var second = new Node();
        try
        {
            Godot.Tree.Root.AddChild(parent);
            using var signal = GodotAssert.ExpectSignal<Node>(parent, Node.SignalName.ChildEnteredTree);
            signal.AssertEmitted(0);
            parent.AddChild(first);
            parent.AddChild(second);
            signal.AssertEmitted(2);
            Assert.That(signal.Values, Is.EqualTo(new[] { first, second }));
            Assert.Throws<AssertionException>(() => signal.AssertEmitted());
        }
        finally
        {
            parent.Free();
            if (GodotObject.IsInstanceValid(first)) first.Free();
            if (GodotObject.IsInstanceValid(second)) second.Free();
        }
    }

    [Test]
    public void DisposalDisconnectsWithoutAsserting()
    {
        var node = new Node();
        try
        {
            var signal = GodotAssert.ExpectSignal(node, Node.SignalName.Renamed);
            signal.Dispose();
            signal.Dispose();
            node.EmitSignal(Node.SignalName.Renamed);
            signal.AssertEmitted(0);
            Assert.That(node.GetSignalConnectionList(Node.SignalName.Renamed), Is.Empty);
        }
        finally { node.Free(); }
    }

    [Test]
    public void SignalTimeoutFailsWithNUnitAssertionAndDisconnects()
    {
        var node = new Node();
        try
        {
            using var signal = GodotAssert.ExpectSignal(node, Node.SignalName.TreeEntered);
            var failure = Assert.ThrowsAsync<AssertionException>(async () =>
                await signal.WaitAsync(Godot, timeout: TimeSpan.FromMilliseconds(10)));
            Assert.That(failure!.Message, Does.Contain("tree_entered"));
            Assert.That(node.GetSignalConnectionList(Node.SignalName.TreeEntered), Is.Empty);
        }
        finally { node.Free(); }
    }

    [Test]
    public void CancellationDisconnectsAndReleasesTheFramePump()
    {
        var node = new Node();
        using var cancellation = new CancellationTokenSource();
        try
        {
            using var signal = GodotAssert.ExpectSignal(node, Node.SignalName.TreeEntered);
            cancellation.CancelAfter(10);
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await signal.WaitAsync(Godot, cancellationToken: cancellation.Token));
            Assert.That(node.GetSignalConnectionList(Node.SignalName.TreeEntered), Is.Empty);
            Assert.DoesNotThrowAsync(async () => await Godot.WaitUntilAsync(() => true));
        }
        finally { node.Free(); }
    }

    [Test]
    public void FreeingSignalSourceFailsPromptlyAndDisposesSafely()
    {
        var node = new Node();
        using var signal = GodotAssert.ExpectSignal(node, Node.SignalName.TreeEntered);
        node.Free();
        var failure = Assert.ThrowsAsync<AssertionException>(async () => await signal.WaitAsync(Godot));
        Assert.That(failure!.Message, Does.Contain("source was freed"));
    }

    [Test]
    public void MissingAndFreedSignalSourcesAreRejected()
    {
        var node = new Node();
        try
        {
            using var missing = new StringName("does_not_exist");
            Assert.Throws<ArgumentException>(() => GodotAssert.ExpectSignal(node, missing));
        }
        finally { node.Free(); }
        Assert.Throws<ArgumentException>(() => GodotAssert.ExpectSignal(node, Node.SignalName.TreeEntered));
    }

    [Test]
    public async Task WaitSupportsMultipleEmissionsAndAlreadyEmittedSignals()
    {
        var node = new Node();
        try
        {
            using var signal = GodotAssert.ExpectSignal(node, Node.SignalName.Renamed);
            Callable.From(() =>
            {
                node.EmitSignal(Node.SignalName.Renamed);
                node.EmitSignal(Node.SignalName.Renamed);
            }).CallDeferred();
            await signal.WaitAsync(Godot, count: 2);
            await signal.WaitAsync(Godot, count: 2);
            signal.AssertEmitted(2);
        }
        finally { node.Free(); }
    }

    [Test]
    public void AwaitPropagatesFailureAndTimeoutDoesNotCancelTheTask()
    {
        var error = new InvalidOperationException("asynchronous failure");
        Assert.That(Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Godot.AwaitAsync(Task.FromException(error))), Is.SameAs(error));
        var pending = new TaskCompletionSource<int>();
        Assert.ThrowsAsync<AssertionException>(async () =>
            await Godot.AwaitAsync(pending.Task, timeout: TimeSpan.FromMilliseconds(10)));
        Assert.That(pending.Task.IsCompleted, Is.False);
        pending.SetResult(42);
        Assert.That(Godot.AwaitAsync(pending.Task).GetAwaiter().GetResult(), Is.EqualTo(42));
    }

    [Test]
    public async Task OverlappingPumpsAreRejectedAndCancellationReleasesThePump()
    {
        using var cancellation = new CancellationTokenSource();
        var first = Godot.WaitUntilAsync(() => false, cancellationToken: cancellation.Token);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Godot.WaitUntilAsync(() => true));
        cancellation.Cancel();
        try { await first; }
        catch (OperationCanceledException) { }
        await Godot.WaitUntilAsync(() => true);
    }

    [Test]
    public void NativeErrorsAndWarningsBecomeNUnitFailures()
    {
        GD.PushWarning("NUnit expected native warning");
        var failure = Assert.Throws<AssertionException>(CheckGodotAfterTest);
        Assert.That(failure!.Message, Does.Contain("NUnit expected native warning"));
        GD.PushError("NUnit expected deferred error");
        failure = Assert.Throws<AssertionException>(CheckGodotBeforeTest);
        Assert.That(failure!.Message, Does.Contain("before this test started"));
        GD.PushError("NUnit consumed error");
        Assert.That(Godot.Errors.Expect("NUnit consumed error"), Has.Length.EqualTo(1));
    }
}
