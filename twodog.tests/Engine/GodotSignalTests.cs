using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.EngineTests;

[Collection<HeadlessCollection>]
public class GodotSignalTests(HeadlessFixture godot)
{
    [Fact]
    public void TreeEntered_Fires_WhenAddedToTree()
    {
        var node = new Node();
        var fired = false;
        node.TreeEntered += () => fired = true;

        godot.Tree.Root.AddChild(node);
        Assert.True(fired);

        node.QueueFree();
        godot.Engine.Iteration();
    }

    [Fact]
    public void TreeExiting_Fires_WhenRemovedFromTree()
    {
        var node = new Node();
        godot.Tree.Root.AddChild(node);

        var fired = false;
        node.TreeExiting += () => fired = true;

        godot.Tree.Root.RemoveChild(node);
        Assert.True(fired);

        node.Free();
    }

    [Fact]
    public void ChildEnteredTree_Fires_ForParent()
    {
        var parent = new Node();
        godot.Tree.Root.AddChild(parent);

        Node? received = null;
        parent.ChildEnteredTree += child => received = child;

        var child = new Node();
        parent.AddChild(child);

        Assert.NotNull(received);
        Assert.Equal(child, received);

        parent.QueueFree();
        godot.Engine.Iteration();
    }

    [Fact]
    public void ChildExitingTree_Fires_ForParent()
    {
        var parent = new Node();
        var child = new Node();
        godot.Tree.Root.AddChild(parent);
        parent.AddChild(child);

        Node? received = null;
        parent.ChildExitingTree += n => received = n;

        parent.RemoveChild(child);

        Assert.NotNull(received);
        Assert.Equal(child, received);

        child.Free();
        parent.QueueFree();
        godot.Engine.Iteration();
    }

    [Fact]
    public void Renamed_Fires_WhenNameChanges()
    {
        var node = new Node { Name = "Before" };
        godot.Tree.Root.AddChild(node);

        var fired = false;
        node.Renamed += () => fired = true;

        node.Name = "After";
        Assert.True(fired);

        node.QueueFree();
        godot.Engine.Iteration();
    }

    [Fact]
    public void CustomSignal_EmitAndReceive_ViaConnect()
    {
        var emitter = new Node();
        godot.Tree.Root.AddChild(emitter);

        // Use a built-in signal name we can emit manually
        var received = false;
        var callable = Callable.From(() => received = true);
        emitter.Connect(Node.SignalName.TreeExiting, callable);

        // Trigger the signal by removing from tree
        godot.Tree.Root.RemoveChild(emitter);
        Assert.True(received);

        emitter.Free();
    }

    [Fact]
    public void ShowcaseSignalTable_SourcesTickOnTheirOwn()
    {
        // The showcase's signal sources emit once per second from their own clocks: C# and GDScript _process, an
        // engine Timer, and the GDExtension's _process override. Every counter must advance without manual ticks.
        var table = godot.Tree.CurrentScene.GetNode("Signals/Table");
        string[] sources = ["CSharpTicker", "GDScriptTicker", "EngineTimer", "GDExtensionTicker"];
        var counters = sources.SelectMany(source => new[] { source + "InCSharp", source + "InGDScript" }).ToArray();

        int Received(string counter)
        {
            if (table.GetNode(counter) is showcase.SignalCounter inCSharp) return inCSharp.Received;
            using var received = table.GetNode(counter).Get("received");
            return received.AsInt32();
        }

        var before = counters.ToDictionary(counter => counter, Received);
        var timeout = TimeSpan.FromSeconds(10);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (counters.Any(counter => Received(counter) <= before[counter]) && clock.Elapsed < timeout)
            godot.Engine.Iteration();

        var stalled = counters.Where(counter => Received(counter) <= before[counter]).ToList();
        Assert.True(stalled.Count == 0,
            $"no signal arrived within {timeout.TotalSeconds} s at: {string.Join(", ", stalled)}");
    }

    [Fact]
    public void MultipleListeners_AllReceiveSignal()
    {
        var node = new Node();

        var count = 0;
        node.TreeEntered += () => count++;
        node.TreeEntered += () => count++;
        node.TreeEntered += () => count++;

        godot.Tree.Root.AddChild(node);
        Assert.Equal(3, count);

        node.QueueFree();
        godot.Engine.Iteration();
    }
}
