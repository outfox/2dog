using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

/// <summary>
/// The generated typed API (phase 2): 1,036 classes from extension_api.json.
/// Exercises every marshalling shape the generator emits - primitives, enums,
/// String/StringName, math structs, object args/returns - plus singleton and
/// registry semantics.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class TypedApiTests(GodotBindingsFixture godot)
{
    [Fact]
    public void Singleton_IsCachedAndLive()
    {
        var a = Godot.Engine.Singleton;
        var b = Godot.Engine.Singleton;
        Assert.Same(a, b);
        Assert.NotEqual(0, a.NativePtr);
    }

    [Fact]
    public void ObjectReturn_MaterializesMostDerivedType()
    {
        var loop = Godot.Engine.Singleton.GetMainLoop();
        Assert.IsType<SceneTree>(loop);
    }

    [Fact]
    public void FloatReturn_Works()
    {
        Assert.True(Godot.Engine.Singleton.GetFramesPerSecond() >= 0);
    }

    [Fact]
    public void IntArgAndReturn_Roundtrip()
    {
        var engine = Godot.Engine.Singleton;
        var original = engine.GetPhysicsTicksPerSecond();
        engine.SetPhysicsTicksPerSecond(120);
        Assert.Equal(120, engine.GetPhysicsTicksPerSecond());
        engine.SetPhysicsTicksPerSecond(original);
    }

    [Fact]
    public void StringNameArg_And_BoolReturn()
    {
        var classDb = ClassDB.Singleton;
        Assert.True(classDb.ClassExists("Node"));
        Assert.False(classDb.ClassExists("DefinitelyNotAClass"));
    }

    [Fact]
    public void StringArgAndReturn_Roundtrip()
    {
        var node = new Node();
        try
        {
            node.SetName("test_node_😀");
            Assert.Equal("test_node_😀", node.GetName());
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void MathStructArgAndReturn_Roundtrip()
    {
        var node = new Node2D();
        try
        {
            node.SetPosition(new Vector2(12.5f, -0.25f));
            var pos = node.GetPosition();
            Assert.Equal(12.5f, pos.X);
            Assert.Equal(-0.25f, pos.Y);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void Vector3AndTransforms_Roundtrip()
    {
        var node = new Node3D();
        try
        {
            node.SetPosition(new Vector3(1, 2, 3));
            var pos = node.GetPosition();
            Assert.Equal(new Vector3(1, 2, 3).X, pos.X);
            Assert.Equal(3f, pos.Z);

            var xf = node.GetTransform();
            Assert.Equal(1f, xf.Basis.Row0.X); // identity basis
            Assert.Equal(1f, xf.Basis.Row1.Y);
            Assert.Equal(new Vector3(1, 2, 3).Y, xf.Origin.Y);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void EnumArgAndReturn_Roundtrip()
    {
        var node = new Node();
        try
        {
            node.SetProcessMode(Node.ProcessMode.PROCESS_MODE_DISABLED);
            Assert.Equal(Node.ProcessMode.PROCESS_MODE_DISABLED, node.GetProcessMode());
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void ObjectArg_Works_InSceneTree()
    {
        var root = ((SceneTree)Godot.Engine.Singleton.GetMainLoop()!).GetRoot()!;
        var before = root.GetChildCount(false);

        var child = new Node();
        child.SetName("typed_api_test_child");
        root.AddChild(child, false, Node.InternalMode.INTERNAL_MODE_DISABLED);
        Assert.Equal(before + 1, root.GetChildCount(false));

        root.RemoveChild(child);
        Assert.Equal(before, root.GetChildCount(false));
        child.Free();
    }

    [Fact]
    public void StaticMethod_Works()
    {
        Assert.True(Godot.FileAccess.FileExists("res://project.godot"));
        Assert.False(Godot.FileAccess.FileExists("res://nope.godot"));
    }

    [Fact]
    public void EditorOnlyClass_CannotConstruct_InTemplateBuild()
    {
        // EditorInterface exists in extension_api.json but not in a template
        // build's ClassDB - the generated guard must throw, not crash.
        var ptr = InstanceBindings.ConstructRaw("EditorPaths");
        Assert.Equal(0, ptr);
    }

    [Fact]
    public void RefCountedReturn_TransfersOwnership()
    {
        // SceneTree.create_timer returns a new SceneTreeTimer (RefCounted):
        // the wrapper must adopt exactly the transferred reference.
        var tree = (SceneTree)Godot.Engine.Singleton.GetMainLoop()!;
        var timer = tree.CreateTimer(1000.0, false, false, false);
        Assert.NotNull(timer);
        Assert.True(timer!.IsRefCounted);
        // engine keeps its own ref while the timer is pending, so rc >= 2
        var rc = timer.GetReferenceCount();
        Assert.True(rc >= 2, $"expected engine + wrapper refs, got {rc}");
        Assert.True(InstanceBindings.DebugIsStrong(timer.NativePtr));
        timer.Dispose();
        DisposalQueue.Drain();
    }

    [Fact]
    public void PumpFrames_Runs()
    {
        var before = Godot.Engine.Singleton.GetProcessFrames();
        godot.PumpFrames(3);
        Assert.True(Godot.Engine.Singleton.GetProcessFrames() >= before + 3);
    }
}
