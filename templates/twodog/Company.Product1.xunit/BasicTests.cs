using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace Company.Product1.Tests;

[Collection<HeadlessCollection>]
public class BasicTests(HeadlessFixture godot)
{
    [Fact]
    public void MainScene_CanBeInstantiated()
    {
        var mainScene = (string)ProjectSettings.GetSetting("application/run/main_scene", "");
        Assert.SkipWhen(mainScene == "", "No run/main_scene configured in project.godot");
        using var scene = GD.Load<PackedScene>(mainScene);

        Assert.NotNull(scene);
        var instance = scene.Instantiate();
        try
        {
            godot.Tree.Root.AddChild(instance);
            Assert.True(instance.IsInsideTree());
            Assert.Same(godot.Tree.Root, instance.GetParent());
        }
        finally { instance.Free(); } // Frees the native subtree, even when an assertion fails.
    }

    [Fact]
    public async Task Await_ReturnsToTheEngineThread()
    {
        var ownerThread = System.Environment.CurrentManagedThreadId;
        await Task.Yield();
        await Task.Delay(1, TestContext.Current.CancellationToken);

        Assert.Equal(ownerThread, System.Environment.CurrentManagedThreadId);
        godot.Engine.Iteration(); // Lifecycle calls also enforce the owner thread.
    }

    [Fact]
    public async Task AwaitGodotSignal_AdvancesTheFrame()
    {
        var ownerThread = System.Environment.CurrentManagedThreadId;
        async Task<int> NextFrame()
        {
            await godot.Tree.ToSignal(godot.Tree, SceneTree.SignalName.ProcessFrame);
            return System.Environment.CurrentManagedThreadId;
        }

        // A raw signal await needs engine frames. AwaitAsync pumps them with a bounded timeout.
        var resumedOn = await godot.AwaitAsync(NextFrame(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ownerThread, resumedOn);
    }

    [Fact]
    public async Task AwaitDeferredWork_ReturnsItsResult()
    {
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Callable.From(() => result.SetResult(42)).CallDeferred();

        Assert.Equal(42, await godot.AwaitAsync(result.Task, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Timer_EmitsTimeout()
    {
        var timer = new Godot.Timer { OneShot = true, WaitTime = 0.01 };
        try
        {
            godot.Tree.Root.AddChild(timer);
            // Subscribe before triggering the behavior, so synchronous emissions cannot be missed.
            using var timeout = GodotAssert.ExpectSignal(timer, Godot.Timer.SignalName.Timeout);
            timer.Start();

            await timeout.WaitAsync(godot, cancellationToken: TestContext.Current.CancellationToken);
            timeout.AssertEmitted();
        }
        finally { timer.Free(); }
    }

    [Fact]
    public void ChildEnteredTree_CarriesTheChild()
    {
        var parent = new Node();
        var child = new Node();
        try
        {
            godot.Tree.Root.AddChild(parent);
            using var entered = GodotAssert.ExpectSignal<Node>(parent, Node.SignalName.ChildEnteredTree);
            parent.AddChild(child);

            entered.AssertEmitted();
            Assert.Same(child, Assert.Single(entered.Values));
        }
        finally
        {
            parent.Free(); // Also frees children still attached to it.
            if (GodotObject.IsInstanceValid(child)) child.Free();
        }
    }

    [Fact]
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

            godot.Tree.Root.AddChild(node);
            entered.AssertEmitted();
            Assert.True(node.IsInsideTree());
            godot.Tree.Root.RemoveChild(node);

            exiting.AssertEmitted();
            exited.AssertEmitted();
            Assert.True(insideWhileExiting);
            Assert.True(outsideWhenExited);
            Assert.False(node.IsInsideTree());
        }
        finally { node.Free(); } // Removing a node from the tree does not free it.
    }

    [Fact]
    public async Task QueueFree_DeletesAfterTheFrame()
    {
        var node = new Node();
        try
        {
            godot.Tree.Root.AddChild(node);
            using var exited = GodotAssert.ExpectSignal(node, Node.SignalName.TreeExited);

            node.QueueFree();
            Assert.True(GodotObject.IsInstanceValid(node));
            Assert.True(node.IsQueuedForDeletion());

            await GodotAssert.FreedAsync(godot, node, cancellationToken: TestContext.Current.CancellationToken);
            exited.AssertEmitted();
            Assert.False(GodotObject.IsInstanceValid(node));
        }
        finally
        {
            if (GodotObject.IsInstanceValid(node)) node.Free();
        }
    }
}
