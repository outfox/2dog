using Godot;

namespace twodog.tests;

[Collection("Godot")]
public class GodotSceneTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void LoadScene_MainScene_ReturnsPackedScene()
    {
        var scene = GD.Load<PackedScene>("res://main.tscn");
        Assert.NotNull(scene);
    }

    [Fact]
    public void InstantiateScene_CreatesCorrectRootType()
    {
        var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();

        Assert.NotNull(instance);
        Assert.IsType<CenterContainer>(instance);

        instance.Free();
    }

    [Fact]
    public void InstantiateScene_HasChildren()
    {
        var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();

        Assert.True(instance.GetChildCount() > 0);

        instance.Free();
    }

    [Fact]
    public void InstantiateScene_ContainsExpectedChildren()
    {
        var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();
        godot.Tree.Root.AddChild(instance);

        Assert.NotNull(instance.FindChild("Ticker"));

        var label = instance.FindChild("TargetLabel");
        Assert.NotNull(label);
        Assert.IsType<Label>(label);

        instance.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void InstantiateScene_LabelHasText()
    {
        var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();
        godot.Tree.Root.AddChild(instance);

        var label = instance.FindChild("TargetLabel") as Label;
        Assert.NotNull(label);
        Assert.False(string.IsNullOrEmpty(label.Text));

        instance.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void InstantiateScene_AddToTree_IsInsideTree()
    {
        var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();
        godot.Tree.Root.AddChild(instance);

        Assert.True(instance.IsInsideTree());

        instance.QueueFree();
        godot.GodotInstance.Iteration();
    }

    [Fact]
    public void PackedScene_CanInstantiateMultipleTimes()
    {
        var scene = GD.Load<PackedScene>("res://main.tscn");

        var a = scene.Instantiate();
        var b = scene.Instantiate();

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);

        a.Free();
        b.Free();
    }
}
