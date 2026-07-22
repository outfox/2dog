using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

// Deliberately no ClassRegistry.Register calls anywhere for these classes:
// they must be registered by the generated [ModuleInitializer].
public partial class AutoRegisteredNode : Node
{
    [Export] public int Score { get; set; } = 7;
}

public class AutoRegisteredPlain : Node2D;

/// <summary>
/// End-to-end auto-registration: the generator's module initializer queued these
/// classes at assembly load and the fixture's engine start flushed the queue.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class AutoRegisterTests
{
    [Fact]
    public void AttributedAndPlainClasses_AreRegistered_WithoutManualCalls()
    {
        Assert.True(ClassRegistry.IsRegistered(typeof(AutoRegisteredNode)));
        Assert.True(ClassRegistry.IsRegistered(typeof(AutoRegisteredPlain)));
    }

    [Fact]
    public void SkippedHierarchy_IsNotRegistered()
    {
        Assert.False(ClassRegistry.IsRegistered(typeof(UnregisteredBase)));
        Assert.False(ClassRegistry.IsRegistered(typeof(OrphanChild)));
    }

    [Fact]
    public void AutoRegisteredClass_IsEngineInstantiable_WithBoundMembers()
    {
        Assert.True(ClassDB.ClassExists("AutoRegisteredNode"));
        using var v = ClassDB.Instantiate("AutoRegisteredNode");
        var node = Assert.IsType<AutoRegisteredNode>(v.AsGodotObject());
        try
        {
            // The generated __BindMembers ran during registration: the engine
            // sees the exported property.
            using var set = (Variant)41;
            node.Set("Score", set);
            Assert.Equal(41, node.Score);
            using var got = node.Get("Score");
            Assert.Equal(41, got.AsInt32());
        }
        finally
        {
            node.Free();
        }
    }
}
