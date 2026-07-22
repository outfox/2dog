using System.Runtime.CompilerServices;
using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

/// <summary>Probe for non-trivial virtual marshalling (math arg, string return).</summary>
public class TooltipControl : Control
{
    public override string _GetTooltip(Vector2 atPosition) => $"tip:{(int)atPosition.X}:{(int)atPosition.Y}";
}

public class UnregisteredBase : Node;

public class OrphanChild : UnregisteredBase;

/// <summary>
/// The load-bearing GC-interaction guarantees of the extension-class model.
/// Failures here are silent state loss or leaks, not exceptions.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class ExtensionClassGcTests(GodotBindingsFixture godot)
{
    private static void EnsureRegistered() => ClassRegistry.Register<TestNode>();

    // #1: the core promise of the strong instance handle - a node's C# state
    // survives GC while only the engine references it.
    [Fact]
    public void StrongHandle_StateSurvivesGc_WhileOnlyEngineOwns()
    {
        EnsureRegistered();
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;

        var id = CreateInTreeUnrooted(root);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        godot.PumpFrames(3);

        var ptr = ObjectFromId(id);
        Assert.NotEqual(0, ptr); // engine object alive (tree owns it)

        var node = Assert.IsType<TestNode>(InstanceBindings.GetOrCreate(ptr, adoptRef: false));
        Assert.Equal("gc_survivor", node.Name);
        // State intact (marker) AND _Process kept incrementing across the GC.
        Assert.True(node.ProcessCalls >= 1003,
            $"expected marker 1000 + >=3 pumped frames, got {node.ProcessCalls}");

        root.RemoveChild(node);
        node.Free();
    }

    private static unsafe nint ObjectFromId(ulong instanceId) =>
        GdExtensionInterface.ObjectGetInstanceFromId(instanceId);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong CreateInTreeUnrooted(Window root)
    {
        var node = new TestNode();
        node.Name = "gc_survivor";
        root.AddChild(node);
        node.ProcessCalls = 1000; // distinctive managed state
        return node.InstanceId;
    }

    // #2: non-trivial virtual marshalling through a real engine roundtrip:
    // the engine invokes _get_tooltip (Vector2 decode) and consumes the
    // returned String (ownership transfer out of the ret slot).
    [Fact]
    public void VirtualDispatch_MathArg_StringReturn_EngineRoundtrip()
    {
        ClassRegistry.Register<TooltipControl>();
        var control = new TooltipControl();
        try
        {
            Assert.Equal("tip:3:-2", control.GetTooltip(new Vector2(3f, -2f)));
        }
        finally
        {
            control.Free();
        }
    }

    // #3: free_instance must release the strong handle - freed nodes' managed
    // objects become collectable, or every freed node leaks its C# object.
    [Fact]
    public void FreedNode_ManagedInstanceBecomesCollectable()
    {
        EnsureRegistered();
        var weak = CreateFreeAndTrack();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive, "freed node's managed instance is still rooted - free_instance leaked the GCHandle");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateFreeAndTrack()
    {
        var node = new TestNode();
        node.Free();
        return new WeakReference(node);
    }

    // #4: engine-returned references resolve to the SAME managed instance.
    [Fact]
    public void EngineReturnedChild_IsSameManagedInstance()
    {
        EnsureRegistered();
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var node = new TestNode();
        try
        {
            root.AddChild(node);
            var back = root.GetChild(root.GetChildCount() - 1);
            Assert.Same(node, back);
            root.RemoveChild(node);
        }
        finally
        {
            node.Free();
        }
    }

    // #5: engine-initiated deferred death (queue_free) detaches the wrapper.
    [Fact]
    public void QueueFree_EngineInitiatedDeath_DetachesWrapper()
    {
        EnsureRegistered();
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var node = new TestNode();
        root.AddChild(node);

        node.QueueFree();
        Assert.True(node.IsValid); // still alive until the deferred flush
        godot.PumpFrames(2);

        Assert.Equal(0, node.NativePtr); // free_instance fired via NativeFreed
        Assert.False(node.IsValid);
    }

    // #6: silent reparenting hole - unregistered intermediate user bases must
    // fail loudly, not quietly parent to the engine class.
    [Fact]
    public void Register_WithUnregisteredUserBase_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(ClassRegistry.Register<OrphanChild>);
        Assert.Contains("UnregisteredBase", ex.Message);
    }
}
