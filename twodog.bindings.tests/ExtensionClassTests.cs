using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

/// <summary>A user-defined extension class, GodotSharp-style: subclass + override.</summary>
public class TestNode : Node
{
    public int ProcessCalls;
    public double LastDelta;
    public bool ReadyCalled;
    public bool EnterTreeCalled;
    public bool ExitTreeCalled;

    public override void _EnterTree() => EnterTreeCalled = true;
    public override void _Ready() => ReadyCalled = true;
    public override void _ExitTree() => ExitTreeCalled = true;

    public override void _Process(double delta)
    {
        ProcessCalls++;
        LastDelta = delta;
    }
}

/// <summary>Derived-from-user-class chain (registered parent is TestNode).</summary>
public class TestNodeChild : TestNode
{
    public override void _Ready()
    {
        base._Ready();
        ChildReadyCalled = true;
    }

    public bool ChildReadyCalled;
}

public class TestResource : Resource;

[Collection(nameof(GodotBindingsCollection))]
public class ExtensionClassTests(GodotBindingsFixture godot)
{
    private static void EnsureRegistered()
    {
        ClassRegistry.Register<TestNode>();
        ClassRegistry.Register<TestNodeChild>();
    }

    [Fact]
    public void Register_ExposesClassInClassDb()
    {
        EnsureRegistered();
        Assert.True(ClassRegistry.IsRegistered(typeof(TestNode)));
        Assert.True(ClassDB.ClassExists("TestNode"));
        Assert.True(ClassDB.IsParentClass("TestNode", "Node"));
    }

    [Fact]
    public void Register_IsIdempotent()
    {
        EnsureRegistered();
        ClassRegistry.Register<TestNode>();
        ClassRegistry.Register<TestNode>();
    }

    [Fact]
    public void NewFromCSharp_ConstructsAsExtensionClass()
    {
        EnsureRegistered();
        var node = new TestNode();
        try
        {
            Assert.NotEqual(0, node.NativePtr);
            Assert.True(node.IsValid);
            Assert.Equal("TestNode", node.GetClass());
            Assert.Same(node, InstanceBindings.GetOrCreate(node.NativePtr, adoptRef: false));
        }
        finally
        {
            node.Free();
        }
        Assert.Equal(0, node.NativePtr);
    }

    [Fact]
    public void EngineInstantiation_CreatesManagedInstance()
    {
        EnsureRegistered();
        var before = ClassRegistry.EngineCreatedInstances;

        // classdb_construct_object3 goes through our create_instance callback.
        var ptr = InstanceBindings.ConstructRaw("TestNode");
        Assert.NotEqual(0, ptr);
        Assert.Equal(before + 1, ClassRegistry.EngineCreatedInstances);

        var wrapper = InstanceBindings.GetOrCreate(ptr, adoptRef: false);
        var node = Assert.IsType<TestNode>(wrapper);
        Assert.Equal("TestNode", node.GetClass());
        node.Free();
    }

    [Fact]
    public void Virtuals_DispatchIntoOverrides()
    {
        EnsureRegistered();
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var node = new TestNode();
        try
        {
            root.AddChild(node);
            Assert.True(node.EnterTreeCalled);
            Assert.True(node.ReadyCalled);

            godot.PumpFrames(5);
            Assert.True(node.ProcessCalls >= 5, $"expected >=5 _Process calls, got {node.ProcessCalls}");
            Assert.True(node.LastDelta > 0);

            root.RemoveChild(node);
            Assert.True(node.ExitTreeCalled);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void DerivedUserClass_ChainsOverridesAndRegistration()
    {
        EnsureRegistered();
        Assert.True(ClassDB.ClassExists("TestNodeChild"));
        Assert.True(ClassDB.IsParentClass("TestNodeChild", "TestNode"));

        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var node = new TestNodeChild();
        try
        {
            Assert.Equal("TestNodeChild", node.GetClass());
            root.AddChild(node);
            Assert.True(node.ReadyCalled);      // base override still runs
            Assert.True(node.ChildReadyCalled); // most-derived override ran
            root.RemoveChild(node);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void UnattachedNode_DoesNotDispatchVirtuals()
    {
        EnsureRegistered();
        var node = new TestNode();
        try
        {
            godot.PumpFrames(2);
            Assert.Equal(0, node.ProcessCalls); // not in tree, no _process
            Assert.False(node.ReadyCalled);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void RefCountedBase_IsSupported()
    {
        ClassRegistry.Register<TestResource>();
        Assert.True(ClassDB.ClassExists("TestResource"));
        using var res = new TestResource();
        Assert.Equal(1, res.GetReferenceCount());
        DisposalQueue.Drain();
    }
}
