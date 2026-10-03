using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.EngineTests;

[Collection<HeadlessCollection>]
public class GodotSceneTests(HeadlessFixture godot)
{
    [Fact]
    public void LoadScene_MainScene_ReturnsPackedScene()
    {
        using var scene = GD.Load<PackedScene>("res://main.tscn");
        Assert.NotNull(scene);
    }

    [Fact]
    public void InstantiateScene_CreatesCorrectRootType()
    {
        using var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();

        Assert.NotNull(instance);
        Assert.IsType<Control>(instance);

        instance.Free();
    }

    [Fact]
    public void InstantiateScene_HasChildren()
    {
        using var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();

        Assert.True(instance.GetChildCount() > 0);

        instance.Free();
    }

    [Fact]
    public void InstantiateScene_ContainsExpectedChildren()
    {
        using var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();
        godot.Tree.Root.AddChild(instance);

        Assert.NotNull(instance.FindChild("BlueCube1"));

        var label = instance.FindChild("TargetLabel");
        Assert.NotNull(label);
        Assert.IsType<Label>(label);

        instance.QueueFree();
        godot.Engine.Iteration();
    }

    [Fact]
    public void InstantiateScene_LabelHasText()
    {
        using var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();
        godot.Tree.Root.AddChild(instance);

        var label = instance.FindChild("TargetLabel") as Label;
        Assert.NotNull(label);
        Assert.False(string.IsNullOrEmpty(label.Text));

        instance.QueueFree();
        godot.Engine.Iteration();
    }

    [Fact]
    public void InstantiateScene_AddToTree_IsInsideTree()
    {
        using var scene = GD.Load<PackedScene>("res://main.tscn");
        var instance = scene.Instantiate();
        godot.Tree.Root.AddChild(instance);

        Assert.True(instance.IsInsideTree());

        instance.QueueFree();
        godot.Engine.Iteration();
    }

    [Fact]
    public void PackedScene_CanInstantiateMultipleTimes()
    {
        using var scene = GD.Load<PackedScene>("res://main.tscn");

        var a = scene.Instantiate();
        var b = scene.Instantiate();

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);

        a.Free();
        b.Free();
    }

    [Fact]
    public void PackedScene_RepeatedLoadsSurviveGarbageCollection()
    {
        // Cached resources are shared with the running scene. Release each managed
        // reference on the engine thread and keep it alive until its instance is freed,
        // so finalizer cleanup cannot race the next load/instantiate's GCHandle swaps.
        for (var i = 0; i < 64; i++)
        {
            showcase.GodotApiSmoke.ImagesAndResources();
            showcase.GodotApiSmoke.SceneAndGeneratedScript();
            GC.Collect();
            using var scene = GD.Load<PackedScene>("res://main.tscn");
            var instance = scene.Instantiate();
            try
            {
                Assert.IsType<Control>(instance);
                Assert.True(instance.GetChildCount() > 0);
            }
            finally
            {
                instance.Free();
            }
            GC.Collect();
        }

        GC.WaitForPendingFinalizers();
    }
}
