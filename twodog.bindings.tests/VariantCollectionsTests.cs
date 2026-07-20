using Godot;
using Godot.NativeInterop;
using Array = Godot.Collections.Array;
using Dictionary = Godot.Collections.Dictionary;

namespace twodog.bindings.tests;

[Collection(nameof(GodotBindingsCollection))]
public class PublicVariantTests
{
    [Fact]
    public void Scalars_RoundtripWithTypeTags()
    {
        using Variant i = 42L;
        Assert.Equal(VariantType.Int, i.VariantType);
        Assert.Equal(42L, i.AsInt64());

        using Variant b = true;
        Assert.Equal(VariantType.Bool, b.VariantType);
        Assert.True(b.AsBool());

        using Variant d = -2.5;
        Assert.Equal(VariantType.Float, d.VariantType);
        Assert.Equal(-2.5, d.AsDouble());

        using Variant s = "höllo 🐶";
        Assert.Equal(VariantType.String, s.VariantType);
        Assert.Equal("höllo 🐶", s.AsString());
    }

    [Fact]
    public void MathTypes_Roundtrip()
    {
        using Variant v2 = new Vector2(1.5f, -2f);
        Assert.Equal(VariantType.Vector2, v2.VariantType);
        Assert.Equal(1.5f, v2.AsVector2().X);

        using Variant v3 = new Vector3(1, 2, 3);
        Assert.Equal(3f, v3.AsVector3().Z);

        using Variant c = new Color(0.25f, 0.5f, 0.75f);
        Assert.Equal(0.5f, c.AsColor().G);
    }

    [Fact]
    public void Objects_RoundtripWithIdentityAndRefcount()
    {
        using var rc = new RefCounted();
        using var v = Variant.From(rc);
        Assert.Equal(VariantType.Object, v.VariantType);
        Assert.Equal(2, rc.GetReferenceCount()); // wrapper + variant
        Assert.Same(rc, v.AsGodotObject());
        Assert.Same(rc, v.As<RefCounted>());
    }

    [Fact]
    public void ObjectSetGet_VariantThroughEngine()
    {
        var node = new Node();
        try
        {
            using Variant name = "via_variant";
            node.Set("name", name); // Object.set(StringName, Variant)
            using var got = node.Get("name");
            Assert.Equal("via_variant", got.AsString());
        }
        finally
        {
            node.Free();
        }
    }
}

[Collection(nameof(GodotBindingsCollection))]
public class CollectionsTests(GodotBindingsFixture godot)
{
    [Fact]
    public void Array_AddIndexCountClear()
    {
        using var arr = new Array();
        Assert.Equal(0, arr.Count);

        using Variant a = 10L;
        using Variant b = "two";
        arr.Add(a);
        arr.Add(b);
        Assert.Equal(2, arr.Count);

        using var e0 = arr[0];
        using var e1 = arr[1];
        Assert.Equal(10L, e0.AsInt64());
        Assert.Equal("two", e1.AsString());

        using Variant repl = 99L;
        arr[0] = repl;
        using var e0b = arr[0];
        Assert.Equal(99L, e0b.AsInt64());
        Assert.True(arr.Contains(repl));

        arr.Clear();
        Assert.Equal(0, arr.Count);
    }

    [Fact]
    public void Array_IndexOutOfRange_Throws()
    {
        using var arr = new Array();
        Assert.Throws<IndexOutOfRangeException>(() => arr[0]);
    }

    [Fact]
    public void Dictionary_SetGetRemove()
    {
        using var dict = new Dictionary();
        using Variant key = "answer";
        using Variant value = 42L;

        dict[key] = value;
        Assert.Equal(1, dict.Count);
        Assert.True(dict.ContainsKey(key));

        using var got = dict[key];
        Assert.Equal(42L, got.AsInt64());

        Assert.True(dict.Remove(key));
        Assert.False(dict.ContainsKey(key));
        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void VariantArray_SharesCowStorage()
    {
        using var arr = new Array();
        using Variant wrapped = Variant.From(arr);
        using var alias = wrapped.AsGodotArray();

        using Variant marker = 7L;
        alias.Add(marker);
        Assert.Equal(1, arr.Count); // same underlying storage through both handles
    }

    [Fact]
    public void EngineDictionary_GetVersionInfo()
    {
        using var info = Godot.Engine.GetVersionInfo();
        using Variant key = "major";
        Assert.True(info.ContainsKey(key));
        using var major = info[key];
        Assert.Equal(4L, major.AsInt64());
    }

    [Fact]
    public void EngineArray_GetChildren_ElementIdentity()
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var child = new Node();
        try
        {
            root.AddChild(child);
            using var children = root.GetChildren(false);
            Assert.Equal(root.GetChildCount(), children.Count);

            using var last = children[children.Count - 1];
            Assert.Same(child, last.AsGodotObject()); // binding identity through Array->Variant->Object
            root.RemoveChild(child);
        }
        finally
        {
            child.Free();
        }
    }

    [Fact]
    public void NodePath_GetNodeAndToString()
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var main = root.GetNode("Main"); // implicit string -> NodePath
        Assert.NotNull(main);
        Assert.Equal("Main", main!.Name);

        using var path = main.GetPath();
        Assert.Equal("/root/Main", path.ToString());
    }
}
