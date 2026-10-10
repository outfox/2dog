using Godot;
using Timer = Godot.Timer;
using twodog.Testing;
using twodog.Testing.NUnit;
using NUnit.Framework;

namespace Company.Product1.Tests;

public class BasicTests : GodotTestFixture
{
    [Test]
    public void MainScene_CanBeInstantiated()
    {
        var mainScene = (string)ProjectSettings.GetSetting("application/run/main_scene", "");
        if (mainScene == "") Assert.Ignore("No run/main_scene configured in project.godot");
        using var scene = GD.Load<PackedScene>(mainScene);

        Assert.That(scene, Is.Not.Null);
        var instance = scene.Instantiate();
        try
        {
            EngineFixture.Tree.Root.AddChild(instance);
            Assert.That(instance.IsInsideTree(), Is.True);
            Assert.That(instance.GetParent(), Is.SameAs(EngineFixture.Tree.Root));
        }
        finally { instance.Free(); } // Frees the native subtree, even when an assertion fails.
    }

    [Test]
    public async Task Await_ReturnsToTheEngineThread()
    {
        var ownerThread = System.Environment.CurrentManagedThreadId;
        await Task.Yield();
        await Task.Delay(1, TestContext.CurrentContext.CancellationToken);

        Assert.That(System.Environment.CurrentManagedThreadId, Is.EqualTo(ownerThread));
        EngineFixture.Engine.Iteration(); // Lifecycle calls also enforce the owner thread.
    }

    [Test]
    public async Task AwaitGodotSignal_AdvancesTheFrame()
    {
        var ownerThread = System.Environment.CurrentManagedThreadId;
        async Task<int> NextFrame()
        {
            await EngineFixture.Tree.ToSignal(EngineFixture.Tree, SceneTree.SignalName.ProcessFrame);
            return System.Environment.CurrentManagedThreadId;
        }

        // A raw signal await needs engine frames. AwaitAsync pumps them with a bounded timeout.
        var resumedOn = await EngineFixture.AwaitAsync(NextFrame(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(resumedOn, Is.EqualTo(ownerThread));
    }

    [Test]
    public async Task AwaitDeferredWork_ReturnsItsResult()
    {
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Callable.From(() => result.SetResult(42)).CallDeferred();

        Assert.That(await EngineFixture.AwaitAsync(result.Task, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.EqualTo(42));
    }

    [Test]
    public async Task Timer_EmitsTimeout()
    {
        var timer = new Timer { OneShot = true, WaitTime = 0.01 };
        try
        {
            EngineFixture.Tree.Root.AddChild(timer);
            // Subscribe before triggering the behavior, so synchronous emissions cannot be missed.
            using var timeout = GodotAssert.ExpectSignal(timer, Timer.SignalName.Timeout);
            timer.Start();

            await timeout.WaitAsync(EngineFixture, cancellationToken: TestContext.CurrentContext.CancellationToken);
            timeout.AssertEmitted();
        }
        finally { timer.Free(); }
    }

    [Test]
    public void ChildEnteredTree_CarriesTheChild()
    {
        var parent = new Node();
        var child = new Node();
        try
        {
            EngineFixture.Tree.Root.AddChild(parent);
            using var entered = GodotAssert.ExpectSignal<Node>(parent, Node.SignalName.ChildEnteredTree);
            parent.AddChild(child);

            entered.AssertEmitted();
            Assert.That(entered.Values, Has.Count.EqualTo(1));
            Assert.That(entered.Values[0], Is.SameAs(child));
        }
        finally
        {
            parent.Free(); // Also frees children still attached to it.
            if (GodotObject.IsInstanceValid(child)) child.Free();
        }
    }

    [Test]
    public void EnteringAndLeavingTree_EmitsLifecycleSignals()
    {
        var node = new Node();
        try
        {
            using var entered = GodotAssert.ExpectSignal(node, Node.SignalName.TreeEntered);
            using var exiting = GodotAssert.ExpectSignal(node, Node.SignalName.TreeExiting);
            using var exited = GodotAssert.ExpectSignal(node, Node.SignalName.TreeExited);
            var insideWhileExiting = false;
            var outsideWhenExited = false;
            node.TreeExiting += () => insideWhileExiting = node.IsInsideTree();
            node.TreeExited += () => outsideWhenExited = !node.IsInsideTree();

            EngineFixture.Tree.Root.AddChild(node);
            entered.AssertEmitted();
            Assert.That(node.IsInsideTree(), Is.True);
            EngineFixture.Tree.Root.RemoveChild(node);

            exiting.AssertEmitted();
            exited.AssertEmitted();
            Assert.That(insideWhileExiting, Is.True);
            Assert.That(outsideWhenExited, Is.True);
            Assert.That(node.IsInsideTree(), Is.False);
        }
        finally { node.Free(); } // Removing a node from the tree does not free it.
    }

    [Test]
    public async Task QueueFree_DeletesAfterTheFrame()
    {
        var node = new Node();
        try
        {
            EngineFixture.Tree.Root.AddChild(node);
            using var exited = GodotAssert.ExpectSignal(node, Node.SignalName.TreeExited);

            node.QueueFree();
            Assert.That(GodotObject.IsInstanceValid(node), Is.True);
            Assert.That(node.IsQueuedForDeletion(), Is.True);

            await GodotAssert.FreedAsync(EngineFixture, node, cancellationToken: TestContext.CurrentContext.CancellationToken);
            exited.AssertEmitted();
            Assert.That(GodotObject.IsInstanceValid(node), Is.False);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(node)) node.Free();
        }
    }
}
