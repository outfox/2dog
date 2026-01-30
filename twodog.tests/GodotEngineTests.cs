using Godot;

namespace twodog.tests;

[Collection("Godot")]
public class GodotEngineTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void Tree_IsNotNull()
    {
        Assert.NotNull(godot.Tree);
    }

    [Fact]
    public void Tree_Root_IsNotNull()
    {
        Assert.NotNull(godot.Tree.Root);
    }

    [Fact]
    public void Engine_Singleton_IsAccessible()
    {
        Assert.NotNull(Godot.Engine.Singleton);
    }

    [Fact]
    public void Engine_IsEditorHint_IsFalse()
    {
        Assert.False(Godot.Engine.IsEditorHint());
    }

    [Fact]
    public void Engine_GetVersionInfo_ContainsExpectedKeys()
    {
        var info = Godot.Engine.GetVersionInfo();
        Assert.True(info.ContainsKey("major"));
        Assert.True(info.ContainsKey("minor"));
        Assert.True(info.ContainsKey("patch"));
        Assert.True(info.ContainsKey("status"));
    }

    [Fact]
    public void Iteration_AdvancesFrameCount()
    {
        var before = Godot.Engine.GetProcessFrames();
        godot.GodotInstance.Iteration();
        var after = Godot.Engine.GetProcessFrames();

        Assert.True(after > before);
    }

    [Fact]
    public void Iteration_ReturnsFalse_WhenNotQuitting()
    {
        var result = godot.GodotInstance.Iteration();
        Assert.False(result);
    }

    [Fact]
    public void OS_GetName_ReturnsNonEmpty()
    {
        var name = OS.GetName();
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Fact]
    public void ClassDb_CanCheckClassExistence()
    {
        Assert.True(ClassDB.ClassExists("Node"));
        Assert.True(ClassDB.ClassExists("SceneTree"));
        Assert.True(ClassDB.ClassExists("RigidBody3D"));
        Assert.False(ClassDB.ClassExists("NonExistentClass"));
    }

    [Fact]
    public void ClassDb_Node_IsParentOfNode3D()
    {
        Assert.True(ClassDB.IsParentClass("Node3D", "Node"));
    }
}
