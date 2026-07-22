using System.Reflection;
using Godot;

namespace twodog.bindings.tests;

/// <summary>
/// GodotSharp-compat property emission: properties are the public surface,
/// accessor methods are hidden (internal), indexed properties route through
/// shared getters, and enums colliding with property names get the "Enum"
/// suffix - all matching official GodotSharp behavior.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class PropertyTests
{
    [Fact]
    public void Property_ReadWrite_Roundtrip()
    {
        var node = new Node2D();
        try
        {
            node.Position = new Vector2(4.5f, 9f);
            Assert.Equal(4.5f, node.Position.X);
            Assert.Equal(9f, node.Position.Y);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void IndexedProperty_RoutesThroughSharedGetter()
    {
        // Light3D.light_energy is get_param(PARAM_ENERGY) behind the scenes.
        var light = new OmniLight3D();
        try
        {
            light.LightEnergy = 2.5f;
            Assert.Equal(2.5f, light.LightEnergy);

            light.LightIndirectEnergy = 0.5f; // different index, same getter
            Assert.Equal(0.5f, light.LightIndirectEnergy);
            Assert.Equal(2.5f, light.LightEnergy); // indexes don't clobber
        }
        finally
        {
            light.Free();
        }
    }

    [Fact]
    public void ReadOnlyProperty_HasNoSetter()
    {
        Assert.Null(typeof(SceneTree).GetProperty("Root")!.SetMethod);
        Assert.NotNull(typeof(SceneTree).GetProperty("Root")!.GetMethod);
    }

    [Fact]
    public void AccessorMethods_AreHiddenFromPublicSurface()
    {
        // GodotSharp compat: the property is the API; accessors are internal.
        Assert.Null(typeof(Node2D).GetMethod("GetPosition", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(Node2D).GetMethod("GetPosition", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(typeof(Node2D).GetProperty("Position"));
    }

    [Fact]
    public void EnumCollidingWithProperty_GetsEnumSuffix()
    {
        Assert.NotNull(typeof(Node).GetProperty("ProcessMode"));
        Assert.NotNull(typeof(Node).GetNestedType("ProcessModeEnum"));
        Assert.Null(typeof(Node).GetNestedType("ProcessMode")); // renamed away
    }

    [Fact]
    public void NodePathProperty_Roundtrips()
    {
        var rt = new RemoteTransform2D();
        try
        {
            rt.RemotePath = "../sibling";
            using var path = rt.RemotePath;
            Assert.Equal("../sibling", path.ToString());
        }
        finally
        {
            rt.Free();
        }
    }

    [Fact]
    public void StringNameProperty_Name_Roundtrips()
    {
        var node = new Node();
        try
        {
            node.Name = "prop_test";
            Assert.Equal("prop_test", node.Name);
        }
        finally
        {
            node.Free();
        }
    }
}
