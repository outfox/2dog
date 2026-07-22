using Godot;

namespace twodog.bindings.tests;

/// <summary>
/// Generated signal events (GodotSharp's flagship ergonomic): typed
/// XEventHandler delegates, += connects, -= disconnects via custom-callable
/// equality keyed on the handler delegate.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class SignalEventTests
{
    [Fact]
    public void Event_SubscribeAndFire()
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var node = new Node();
        try
        {
            root.AddChild(node);
            var renames = 0;
            node.Renamed += () => renames++;

            node.Name = "event_rename_one";
            node.Name = "event_rename_two";
            Assert.Equal(2, renames);
            root.RemoveChild(node);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void Event_Unsubscribe_Disconnects()
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var node = new Node();
        try
        {
            root.AddChild(node);
            var renames = 0;
            Node.RenamedEventHandler handler = () => renames++;

            node.Renamed += handler;
            node.Name = "unsub_first";
            Assert.Equal(1, renames);

            // -= must match the += connection: custom-callable equality is
            // keyed on the handler delegate, not the Callable instance.
            node.Renamed -= handler;
            node.Name = "unsub_second";
            Assert.Equal(1, renames);
            root.RemoveChild(node);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void Event_TypedObjectArg_ResolvesIdentity()
    {
        var tree = (SceneTree)Godot.Engine.GetMainLoop()!;
        var root = tree.Root!;
        Node? observed = null;
        SceneTree.NodeAddedEventHandler handler = n => observed = n;
        tree.NodeAdded += handler;
        var node = new Node();
        try
        {
            root.AddChild(node);
            Assert.Same(node, observed); // typed signal arg through the binding
            root.RemoveChild(node);
        }
        finally
        {
            tree.NodeAdded -= handler;
            node.Free();
        }
    }

    [Fact]
    public void Event_MultipleSubscribers_AllFire()
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var node = new Node();
        try
        {
            root.AddChild(node);
            var a = 0;
            var b = 0;
            node.Renamed += () => a++;
            node.Renamed += () => b++;
            node.Name = "multi_sub";
            Assert.Equal(1, a);
            Assert.Equal(1, b);
            root.RemoveChild(node);
        }
        finally
        {
            node.Free();
        }
    }
}

public class MathfTests
{
    [Fact]
    public void Basics()
    {
        Assert.Equal(90f, Mathf.RadToDeg(Mathf.Pi / 2));
        Assert.True(Mathf.IsEqualApprox(Mathf.DegToRad(180f), Mathf.Pi));
        Assert.Equal(5f, Mathf.Lerp(0f, 10f, 0.5f));
        Assert.Equal(3, Mathf.Clamp(7, 0, 3));
        Assert.Equal(1, Mathf.PosMod(-3, 4));
        Assert.Equal(2f, Mathf.Snapped(2.2f, 1f));
        Assert.Equal(7f, Mathf.MoveToward(5f, 10f, 2f));
        Assert.True(Mathf.IsZeroApprox(1e-7f));
        Assert.Equal(4, Mathf.Wrap(14, 0, 10));
    }
}
