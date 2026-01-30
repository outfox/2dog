using Godot;

namespace twodog.tests;

[Collection("Godot")]
public class GodotNodeTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void Node_SetName_ChangesName()
    {
        var node = new Node();
        node.Name = "TestNode";
        Assert.Equal("TestNode", (string)node.Name);
        node.Free();
    }

    [Fact]
    public void AddChild_IncreasesChildCount()
    {
        var parent = new Node();
        var child = new Node();
        godot.Tree.Root.AddChild(parent);

        Assert.Equal(0, parent.GetChildCount());
        parent.AddChild(child);
        Assert.Equal(1, parent.GetChildCount());

        parent.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void AddChild_SetsParent()
    {
        var parent = new Node();
        var child = new Node();
        godot.Tree.Root.AddChild(parent);
        parent.AddChild(child);

        Assert.Equal(parent, child.GetParent());

        parent.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void RemoveChild_DecreasesChildCount()
    {
        var parent = new Node();
        var child = new Node();
        godot.Tree.Root.AddChild(parent);
        parent.AddChild(child);

        Assert.Equal(1, parent.GetChildCount());
        parent.RemoveChild(child);
        Assert.Equal(0, parent.GetChildCount());

        child.Free();
        parent.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void GetChild_ReturnsCorrectChild()
    {
        var parent = new Node();
        var childA = new Node { Name = "A" };
        var childB = new Node { Name = "B" };
        godot.Tree.Root.AddChild(parent);
        parent.AddChild(childA);
        parent.AddChild(childB);

        Assert.Equal(childA, parent.GetChild(0));
        Assert.Equal(childB, parent.GetChild(1));

        parent.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void GetPath_ReturnsValidPath_WhenInTree()
    {
        var node = new Node { Name = "PathTest" };
        godot.Tree.Root.AddChild(node);

        var path = node.GetPath();
        Assert.Contains("PathTest", (string)path);

        node.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void IsInsideTree_FalseBeforeAdding_TrueAfter()
    {
        var node = new Node();
        Assert.False(node.IsInsideTree());

        godot.Tree.Root.AddChild(node);
        Assert.True(node.IsInsideTree());

        node.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void AddToGroup_IsInGroup()
    {
        var node = new Node();
        godot.Tree.Root.AddChild(node);

        node.AddToGroup("test_group");
        Assert.True(node.IsInGroup("test_group"));

        var nodesInGroup = godot.Tree.GetNodesInGroup("test_group");
        Assert.Contains(node, nodesInGroup);

        node.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void QueueFree_RemovesNodeAfterIteration()
    {
        var node = new Node { Name = "ToBeFreed" };
        godot.Tree.Root.AddChild(node);

        node.QueueFree();
        godot.GodotInstance.Iteration();

        var found = godot.Tree.Root.FindChild("ToBeFreed");
        Assert.Null(found);
    }

    [Fact]
    public void Duplicate_CreatesIndependentCopy()
    {
        var original = new Node { Name = "Original" };
        godot.Tree.Root.AddChild(original);

        var copy = original.Duplicate();
        Assert.NotNull(copy);
        godot.Tree.Root.AddChild(copy);
        Assert.NotEqual(original, copy);

        original.QueueFree();
        copy.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void SetMeta_GetMeta_RoundTrips()
    {
        var node = new Node();
        node.SetMeta("score", 42);

        Assert.True(node.HasMeta("score"));
        Assert.Equal(42, (int)node.GetMeta("score"));

        node.Free();
    }

    [Fact]
    public void Node3D_Position_DefaultsToZero()
    {
        var node = new Node3D();
        Assert.Equal(Vector3.Zero, node.Position);
        node.Free();
    }

    [Fact]
    public void Node3D_SetPosition_UpdatesValue()
    {
        var node = new Node3D();
        var pos = new Vector3(1, 2, 3);
        node.Position = pos;
        Assert.Equal(pos, node.Position);
        node.Free();
    }
}
