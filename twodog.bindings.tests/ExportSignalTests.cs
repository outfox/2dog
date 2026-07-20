using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

/// <summary>A user class exactly as a GodotSharp user would write it.</summary>
public partial class ExportNode : Node
{
    [Export] public float Speed { get; set; } = 5f;
    [Export] public string Title { get; set; } = "untitled";
    [Export(PropertyHint.Range, "0,100")] public int Health { get; set; } = 100;
    [Export] public Node? Target { get; set; }

    [Signal] public delegate void ScoredEventHandler(long points, string by);
    [Signal] public delegate void DiedEventHandler();
}

/// <summary>
/// The [Export]/[Signal] source generator end-to-end: generated __BindMembers
/// registers properties (engine Set/Get roundtrips through the generated
/// accessors) and signals (HasSignal, generated events, EmitSignalX).
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class ExportSignalTests
{
    private static void EnsureRegistered() => ClassRegistry.Register<ExportNode>();

    [Fact]
    public void ExportedProperty_EngineGet_ReadsInitialValue()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            using var speed = node.Get("speed");   // engine -> registered getter
            Assert.Equal(5.0, speed.AsDouble());
            using var title = node.Get("title");
            Assert.Equal("untitled", title.AsString());
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void ExportedProperty_EngineSet_WritesThroughSetter()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            using Variant v = 7.5;
            node.Set("speed", v);                  // engine -> registered setter
            Assert.Equal(7.5f, node.Speed);        // landed on the C# property

            using Variant t = "renamed title";
            node.Set("title", t);
            Assert.Equal("renamed title", node.Title);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void ExportedObjectProperty_RoundtripsIdentity()
    {
        EnsureRegistered();
        var node = new ExportNode();
        var other = new Node();
        try
        {
            using var target = Variant.From(other);
            node.Set("target", target);
            Assert.Same(other, node.Target);

            using var back = node.Get("target");
            Assert.Same(other, back.AsGodotObject());
        }
        finally
        {
            other.Free();
            node.Free();
        }
    }

    [Fact]
    public void ExportedProperty_AppearsInPropertyList()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            using var list = node.GetPropertyList();
            var found = false;
            for (var i = 0; i < list.Count; i++)
            {
                using var entry = list[i];
                using var dict = entry.AsGodotDictionary();
                using Variant key = "name";
                using var name = dict[key];
                if (name.AsString() == "health") found = true;
            }
            Assert.True(found, "exported property 'health' missing from get_property_list");
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void Signal_IsRegistered()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            Assert.True(node.HasSignal("scored"));
            Assert.True(node.HasSignal("died"));
            Assert.False(node.HasSignal("nonexistent"));
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void Signal_GeneratedEventAndEmit_Roundtrip()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            long points = 0;
            string? by = null;
            var died = 0;
            node.Scored += (p, b) =>
            {
                points = p;
                by = b;
            };
            node.Died += () => died++;

            node.EmitSignalScored(42, "tester");
            node.EmitSignalDied();

            Assert.Equal(42, points);
            Assert.Equal("tester", by);
            Assert.Equal(1, died);
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void Signal_GeneratedEventUnsubscribe_Disconnects()
    {
        EnsureRegistered();
        var node = new ExportNode();
        try
        {
            var count = 0;
            ExportNode.DiedEventHandler handler = () => count++;
            node.Died += handler;
            node.EmitSignalDied();
            node.Died -= handler;
            node.EmitSignalDied();
            Assert.Equal(1, count);
        }
        finally
        {
            node.Free();
        }
    }
}
