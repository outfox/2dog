using Godot;
using twodog.fixture;

namespace twodog.tests;

// Engine restart across test collections.
//
// Each collection below gets its OWN GodotHeadlessFixture. All Godot
// collections in this assembly disable parallelization, so xUnit runs them
// sequentially and disposes one collection's fixture before creating the
// next. Together with the shared GodotHeadlessCollection used by the other
// test classes, this assembly therefore starts and destroys the engine
// several times in one process - verifying that libgodot reinitialization
// (destroy a GodotInstance, create a new one) works end to end.

[CollectionDefinition(nameof(RestartCollectionA), DisableParallelization = true)]
public class RestartCollectionA : ICollectionFixture<GodotHeadlessFixture>;

[CollectionDefinition(nameof(RestartCollectionB), DisableParallelization = true)]
public class RestartCollectionB : ICollectionFixture<GodotHeadlessFixture>;

// The same smoke tests run in both collections, i.e. against two different
// engine instances. They deliberately exercise the areas that are fragile
// across a restart: engine singletons, StringName-backed calls, resource
// loading, scene-tree manipulation, and main-loop iteration.
public abstract class GodotRestartSmokeTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void Tree_IsAlive()
    {
        Assert.NotNull(godot.Tree);
        Assert.NotNull(godot.Tree.Root);
    }

    [Fact]
    public void EngineSingleton_IsAccessible()
    {
        Assert.NotNull(Godot.Engine.Singleton);
        var info = Godot.Engine.GetVersionInfo();
        Assert.True(info.ContainsKey("major"));
    }

    [Fact]
    public void Scene_CanLoadInstantiateAndFree()
    {
        var scene = GD.Load<PackedScene>("res://main.tscn");
        Assert.NotNull(scene);

        var instance = scene.Instantiate();
        godot.Tree.Root.AddChild(instance);
        Assert.True(GodotObject.IsInstanceValid(instance));

        godot.Tree.Root.RemoveChild(instance);
        instance.Free();
    }

    [Fact]
    public void Node_StringNameApis_Work()
    {
        var node = new Node { Name = "RestartProbe" };
        godot.Tree.Root.AddChild(node);

        Assert.Equal("RestartProbe", (string)node.Name);
        Assert.NotNull(godot.Tree.Root.GetNodeOrNull("RestartProbe"));

        godot.Tree.Root.RemoveChild(node);
        node.Free();
    }

    [Fact]
    public void MainLoop_Iterates()
    {
        // Iteration() returns true when the engine wants to quit.
        Assert.False(godot.GodotInstance.Iteration());
    }
}

[Collection(nameof(RestartCollectionA))]
public class GodotRestartATests(GodotHeadlessFixture godot) : GodotRestartSmokeTests(godot);

[Collection(nameof(RestartCollectionB))]
public class GodotRestartBTests(GodotHeadlessFixture godot) : GodotRestartSmokeTests(godot);
