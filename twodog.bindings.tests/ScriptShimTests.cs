using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

/// <summary>
/// The script-language shim end-to-end: a .tscn referencing res://*.cs attaches
/// the managed class to a plain engine node (GodotSharp's model), scene-saved
/// exported values apply, engine virtuals pump, and GDScript neighbors talk to
/// the C# script through the engine - both directions.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class ScriptShimTests(GodotBindingsFixture godot)
{
    private static ShimScriptNode LoadScene(out Node root)
    {
        var packed = (PackedScene?)ResourceLoader.Load("res://shim_scene.tscn");
        Assert.NotNull(packed);
        root = packed!.Instantiate()!;
        return Assert.IsType<ShimScriptNode>(root);
    }

    [Fact]
    public void SceneLoad_AttachesManagedScript_AndAppliesExportedValue()
    {
        var node = LoadScene(out var root);
        try
        {
            Assert.Equal(41, node.Sprouts); // 3 in C#, 41 saved in the scene
            Assert.Equal("Node", node.GetClass()); // owner keeps its engine class
        }
        finally
        {
            root.Free();
        }
    }

    [Fact]
    public void EngineVirtuals_ReachTheScript_ReadyAndProcess()
    {
        var node = LoadScene(out var root);
        var tree = (SceneTree)Godot.Engine.GetMainLoop()!;
        try
        {
            tree.Root!.AddChild(node);
            Assert.True(node.ReadyRan, "_Ready did not run on tree entry");
            godot.PumpFrames(3);
            Assert.True(node.Frames > 0, "_Process not pumped (has_method gate broken?)");
        }
        finally
        {
            tree.Root!.RemoveChild(node);
            root.Free();
        }
    }

    [Fact]
    public void EngineSetGet_RouteThroughScriptInstance()
    {
        var node = LoadScene(out var root);
        try
        {
            using Variant v = 100;
            node.Set("Sprouts", v);
            Assert.Equal(100, node.Sprouts);
            using var got = node.Get("Sprouts");
            Assert.Equal(100, got.AsInt32());
            Assert.True(node.HasMethod("Bump"));
            Assert.False(node.HasMethod("NoSuchMethod"));

            // Typed-array export roundtrip through the script instance.
            using var tags = new Godot.Collections.Array();
            using Variant tag = "leafy";
            tags.Add(tag);
            using var tagsVariant = Variant.From(tags);
            node.Set("Tags", tagsVariant);
            Assert.Equal(["leafy"], node.Tags);
            using var back = node.Get("Tags");
            using var backArr = back.AsGodotArray();
            Assert.Equal(1, backArr.Count);
        }
        finally
        {
            root.Free();
        }
    }

    [Fact]
    public void GdScript_ReadsProperties_AndCallsMethods_OnCSharpScript()
    {
        var node = LoadScene(out var root);
        try
        {
            var caller = node.GetNode("Caller")!;

            using var read = caller.Call("read_sprouts");
            Assert.Equal(41, read.AsInt32());

            using Variant by = 2;
            using var bumped = caller.Call("call_bump", by);
            Assert.Equal(43, bumped.AsInt32());
            Assert.Equal(43, node.Sprouts);
        }
        finally
        {
            root.Free();
        }
    }

    [Fact]
    public void SceneConnection_OnScriptDeclaredSignal_ReachesGdScript()
    {
        var node = LoadScene(out var root);
        try
        {
            var caller = node.GetNode("Caller")!;
            using var before = caller.Call("was_sprouted");
            Assert.False(before.AsBool());

            node.EmitSignalSprouted();

            using var after = caller.Call("was_sprouted");
            Assert.True(after.AsBool(), "scene [connection] on a C# script signal did not fire");
        }
        finally
        {
            root.Free();
        }
    }
}
