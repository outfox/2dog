using Godot;
using twodog.xunit;

namespace Company.Product1.Tests;

[Collection("GodotHeadless")]
public class BasicTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void LoadMainScene_Succeeds()
    {
        // Arrange
        var scene = GD.Load<PackedScene>("res://main.tscn");
        
        // Act
        var instance = scene.Instantiate();
        godot.Tree.Root.AddChild(instance);
        
        // Assert
        Assert.NotNull(instance);
        Assert.NotNull(instance.Parent);
    }
    
    [Fact]
    public void PhysicsIteration_Succeeds()
    {
        // Arrange & Act
        godot.Tree.Root.PhysicsInterpolation = false;
        godot.Instance.Iteration();

        // Assert - if we get here without crashing, test passes
        Assert.True(true);
    }

    [Fact]
    public void CreateNode_AddsToTree()
    {
        // Arrange
        var node = new Node();
        node.Name = "TestNode";

        // Act
        godot.Tree.Root.AddChild(node);

        // Assert
        Assert.True(godot.Tree.Root.HasNode("TestNode"));
        Assert.Equal("TestNode", (string)godot.Tree.Root.GetNode("TestNode").Name);

        // Cleanup
        node.QueueFree();
    }
}
