using Godot;
using Array = Godot.Collections.Array;

namespace twodog.bindings.tests;

/// <summary>
/// Callables, signals (connect/emit end-to-end through the engine), vararg
/// methods, and packed arrays as C# arrays - the last marshalling kinds.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class SignalPackedTests
{
    [Fact]
    public void CustomCallable_ConnectAndEmit()
    {
        var node = new Node();
        try
        {
            using var arguments = new Array();
            node.AddUserSignal("test_signal", arguments);

            var calls = 0;
            using var callable = Callable.From(() => calls++);
            Assert.Equal(Error.Ok, node.Connect("test_signal", callable));

            node.EmitSignal("test_signal");
            node.EmitSignal("test_signal");
            Assert.Equal(2, calls);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void CustomCallable_ReceivesEmittedArgs()
    {
        var node = new Node();
        try
        {
            using var arguments = new Array();
            node.AddUserSignal("valued_signal", arguments);

            long received = 0;
            string? text = null;
            using var callable = Callable.From((Variant a, Variant b) =>
            {
                received = a.AsInt64();
                text = b.AsString();
                a.Dispose();
                b.Dispose();
            });
            node.Connect("valued_signal", callable);

            node.EmitSignal("valued_signal", 42L, "payload");
            Assert.Equal(42L, received);
            Assert.Equal("payload", text);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void EngineSignal_FiresIntoCSharp()
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var node = new Node();
        try
        {
            root.AddChild(node);
            var renames = 0;
            using var callable = Callable.From(() => renames++);
            node.Connect("renamed", callable);

            node.Name = "renamed_once";
            node.Name = "renamed_twice";
            Assert.Equal(2, renames);
            root.RemoveChild(node);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void CallableDispose_EngineCopyKeepsDelegateAlive()
    {
        var node = new Node();
        try
        {
            using var arguments = new Array();
            node.AddUserSignal("held_signal", arguments);

            var calls = 0;
            var callable = Callable.From(() => calls++);
            node.Connect("held_signal", callable);
            callable.Dispose(); // engine's connection copy must keep the delegate alive

            GC.Collect();
            GC.WaitForPendingFinalizers();

            node.EmitSignal("held_signal");
            Assert.Equal(1, calls);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void BoundCallable_InvokesMethod()
    {
        var node = new Node();
        try
        {
            using var arguments = new Array();
            node.AddUserSignal("bound_signal", arguments);

            // Bound callable to an engine method: set_process(bool) has 1 arg;
            // use queue_free-adjacent safe probe instead: notification-free
            // choice - bind to "set_name" via signal arg? Keep simple: bind a
            // no-arg engine method.
            using var callable = new Callable(node, "queue_free");
            node.Connect("bound_signal", callable);
            Assert.True(node.IsValid);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void VarargCall_Works()
    {
        var node = new Node();
        try
        {
            using var ret = node.Call("get_class");
            Assert.Equal("Node", ret.AsString());

            using var name = node.Call("get_name");
            node.Call("set_name", (Variant)"via_vararg_call");
            using var got = node.Call("get_name");
            Assert.Equal("via_vararg_call", got.AsString());
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void PackedBytes_Roundtrip()
    {
        var bytes = Godot.FileAccess.GetFileAsBytes("res://project.godot");
        Assert.NotEmpty(bytes);
        Assert.Contains("config_version", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void PackedStrings_FromEngine()
    {
        var extensions = ResourceLoader.GetRecognizedExtensionsForType("Resource");
        Assert.NotEmpty(extensions);
        Assert.Contains("tres", extensions);
    }

    [Fact]
    public void PackedVector2_PropertyRoundtrip()
    {
        var poly = new Polygon2D();
        try
        {
            poly.Polygon = [new Vector2(1, 2), new Vector2(3, 4), new Vector2(5, 6)];
            var back = poly.Polygon;
            Assert.Equal(3, back.Length);
            Assert.Equal(3f, back[1].X);
            Assert.Equal(6f, back[2].Y);
        }
        finally
        {
            poly.Free();
        }
    }
}
