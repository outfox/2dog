using Godot;
using Godot.NativeInterop;
using Array = Godot.Collections.Array;
using Dictionary = Godot.Collections.Dictionary;

namespace twodog.bindings.tests;

public enum Team
{
    Neutral,
    Red,
    Blue,
}

/// <summary>Exercises the broadened [Export]/[Signal] type support.</summary>
public partial class BroadNode : Node
{
    [Export] public Team Team { get; set; } = Team.Red;
    [Export] public Variant Payload { get; set; }
    [Export] public Array? Items { get; set; }
    [Export] public Dictionary? Stats { get; set; }
    [Export] public NodePath? TargetPath { get; set; }
    [Export] public StringName? Group { get; set; }

    [Signal] public delegate void LoadedEventHandler(Team team, Variant payload, Godot.Collections.Array items);
}

[Collection(nameof(GodotBindingsCollection))]
public class ExportBroadTests
{
    private static void EnsureRegistered() => ClassRegistry.Register<BroadNode>();

    [Fact]
    public void EnumExport_EngineRoundtrip()
    {
        EnsureRegistered();
        var node = new BroadNode();
        try
        {
            using var got = node.Get("Team");
            Assert.Equal((long)Team.Red, got.AsInt64());

            using Variant v = (long)Team.Blue;
            node.Set("Team", v);
            Assert.Equal(Team.Blue, node.Team);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void EnumExport_GetsEnumHintWithMemberNames()
    {
        EnsureRegistered();
        var node = new BroadNode();
        try
        {
            using var list = node.GetPropertyList();
            string? hintString = null;
            long hint = 0;
            for (var i = 0; i < list.Count; i++)
            {
                using var entry = list[i];
                using var dict = entry.AsGodotDictionary();
                using Variant nameKey = "name";
                using var name = dict[nameKey];
                if (name.AsString() != "Team") continue;
                using Variant hintKey = "hint";
                using Variant hintStringKey = "hint_string";
                using var h = dict[hintKey];
                using var hs = dict[hintStringKey];
                hint = h.AsInt64();
                hintString = hs.AsString();
            }
            Assert.Equal(2, hint); // PropertyHint.Enum
            Assert.Equal("Neutral,Red,Blue", hintString);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void ObjectExport_CarriesClassNameAndNodeTypeHint()
    {
        ClassRegistry.Register<ExportNode>();
        var node = new ExportNode();
        try
        {
            using var list = node.GetPropertyList();
            string? className = null;
            string? hintString = null;
            long hint = -1;
            for (var i = 0; i < list.Count; i++)
            {
                using var entry = list[i];
                using var dict = entry.AsGodotDictionary();
                using Variant nameKey = "name";
                using var name = dict[nameKey];
                if (name.AsString() != "Target") continue;
                using Variant classNameKey = "class_name";
                using Variant hintKey = "hint";
                using Variant hintStringKey = "hint_string";
                using var cn = dict[classNameKey];
                using var h = dict[hintKey];
                using var hs = dict[hintStringKey];
                className = cn.AsString();
                hint = h.AsInt64();
                hintString = hs.AsString();
            }
            Assert.Equal("Node", className);
            Assert.Equal(34, hint); // PropertyHint.NodeType
            Assert.Equal("Node", hintString);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void VariantExport_HoldsAnyType()
    {
        EnsureRegistered();
        var node = new BroadNode();
        try
        {
            using Variant s = "any value";
            node.Set("Payload", s);
            Assert.Equal("any value", node.Payload.AsString());

            using Variant n = 42L;
            node.Set("Payload", n);
            using var got = node.Get("Payload");
            Assert.Equal(42L, got.AsInt64());
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void CollectionExports_Roundtrip()
    {
        EnsureRegistered();
        var node = new BroadNode();
        try
        {
            using var items = new Array();
            using Variant item = "sword";
            items.Add(item);
            using var itemsVariant = Variant.From(items);
            node.Set("Items", itemsVariant);

            Assert.NotNull(node.Items);
            Assert.Equal(1, node.Items!.Count);
            using var e0 = node.Items[0];
            Assert.Equal("sword", e0.AsString());

            using var stats = new Dictionary();
            using Variant key = "hp";
            using Variant value = 10L;
            stats[key] = value;
            using var statsVariant = Variant.From(stats);
            node.Set("Stats", statsVariant);
            Assert.Equal(1, node.Stats!.Count);

            // Null collection reads back as NIL, not a crash.
            node.Items = null;
            using var nil = node.Get("Items");
            Assert.Equal(VariantType.Nil, nil.VariantType);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void StringNameAndNodePathExports_Roundtrip()
    {
        EnsureRegistered();
        var node = new BroadNode();
        try
        {
            node.Group = "enemies"; // implicit string -> StringName
            using var got = node.Get("Group");
            Assert.Equal("enemies", got.AsStringName());

            using Variant path = Variant.From((NodePath)"../target");
            node.Set("TargetPath", path);
            Assert.Equal("../target", node.TargetPath!.ToString());
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void Signal_WithBroadTypes_Roundtrips()
    {
        EnsureRegistered();
        var node = new BroadNode();
        try
        {
            Team team = Team.Neutral;
            long payload = 0;
            var itemCount = -1;
            node.Loaded += (t, p, items) =>
            {
                team = t;
                payload = p.AsInt64();
                itemCount = items.Count;
                p.Dispose();
                items.Dispose();
            };

            using var arr = new Array();
            using Variant item = 1L;
            arr.Add(item);
            node.EmitSignalLoaded(Team.Blue, 7L, arr);

            Assert.Equal(Team.Blue, team);
            Assert.Equal(7L, payload);
            Assert.Equal(1, itemCount);
        }
        finally
        {
            node.Free();
        }
    }
}

/// <summary>StringName parameter/return typing (GodotSharp compat shape).</summary>
[Collection(nameof(GodotBindingsCollection))]
public class StringNameTypingTests
{
    [Fact]
    public void Name_IsStringNameTyped_WithImplicitStringCompat()
    {
        var node = new Node();
        try
        {
            node.Name = "sn_typed";                       // string -> StringName implicit
            StringName name = node.Name;                  // property is StringName-typed
            Assert.Equal("sn_typed", name.ToString());
            Assert.Equal("sn_typed", (string)node.Name);  // implicit back to string
            Assert.True(node.Name == "sn_typed");         // equality via implicit conversion
            Assert.Equal(typeof(StringName), typeof(Node).GetProperty("Name")!.PropertyType);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void StringName_Interning_MakesEqualityCheap()
    {
        StringName a = "interned_typed_name";
        StringName b = "interned_typed_name";
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotSame(a, b); // different instances, same interned payload
    }

    [Fact]
    public void StringNameParams_AcceptStringLiterals()
    {
        // ClassExists(StringName) called with a literal - the compat crux.
        Assert.True(ClassDB.ClassExists("Node"));
        var node = new Node();
        try
        {
            Assert.True(node.HasMethod("get_child_count"));
            Assert.False(node.HasMethod("no_such_method"));
        }
        finally
        {
            node.Free();
        }
    }
}
