using Godot;

namespace twodog.tests;

[Collection("Godot")]
public class GodotPhysicsTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void PhysicsServer3D_Singleton_IsAvailable()
    {
        Assert.NotNull(PhysicsServer3D.Singleton);
    }

    [Fact]
    public void PhysicsServer2D_Singleton_IsAvailable()
    {
        Assert.NotNull(PhysicsServer2D.Singleton);
    }

    [Fact]
    public void PhysicsServer3D_SpaceCreate_ReturnsValidRid()
    {
        var space = PhysicsServer3D.SpaceCreate();
        Assert.True(space.IsValid);
        PhysicsServer3D.FreeRid(space);
    }

    [Fact]
    public void PhysicsServer3D_BodyCreate_ReturnsValidRid()
    {
        var body = PhysicsServer3D.BodyCreate();
        Assert.True(body.IsValid);
        PhysicsServer3D.FreeRid(body);
    }

    [Fact]
    public void PhysicsServer2D_SpaceCreate_ReturnsValidRid()
    {
        var space = PhysicsServer2D.SpaceCreate();
        Assert.True(space.IsValid);
        PhysicsServer2D.FreeRid(space);
    }

    [Fact]
    public void PhysicsServer2D_BodyCreate_ReturnsValidRid()
    {
        var body = PhysicsServer2D.BodyCreate();
        Assert.True(body.IsValid);
        PhysicsServer2D.FreeRid(body);
    }

    [Fact]
    public void SphereShape3D_SetRadius_UpdatesValue()
    {
        var shape = new SphereShape3D();
        shape.Radius = 5.0f;
        Assert.Equal(5.0f, shape.Radius);
    }

    [Fact]
    public void BoxShape3D_SetSize_UpdatesValue()
    {
        var shape = new BoxShape3D();
        var size = new Vector3(2, 3, 4);
        shape.Size = size;
        Assert.Equal(size, shape.Size);
    }

    [Fact]
    public void RigidBody3D_Create_HasDefaultProperties()
    {
        var body = new RigidBody3D();
        Assert.Equal(1.0f, body.Mass);
        Assert.Equal(Vector3.Zero, body.LinearVelocity);
        Assert.Equal(Vector3.Zero, body.AngularVelocity);
        body.Free();
    }

    [Fact]
    public void StaticBody3D_AddToTree_IsInsideTree()
    {
        var body = new StaticBody3D();
        godot.Tree.Root.AddChild(body);

        Assert.True(body.IsInsideTree());

        body.QueueFree();
        godot.GodotInstance.Iteration();
    }
}
