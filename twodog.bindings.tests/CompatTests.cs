using Godot;

namespace twodog.bindings.tests;

/// <summary>
/// Pins the GodotSharp-compat surface shapes: GD statics, static singleton
/// classes, renamed enum members, and default arguments.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class CompatTests
{
    [Fact]
    public void GD_Print_AcceptsVariants()
    {
        using var mute = new EngineOutputMute();
        GD.Print((Variant)"GD.Print from twodog.bindings", (Variant)42L);
        GD.PrintErr((Variant)"GD.PrintErr works too");
    }

    [Fact]
    public void GD_VarToStr_StrToVar_Roundtrip()
    {
        using Variant v = 42L;
        Assert.Equal("42", GD.VarToStr(v));

        using var back = GD.StrToVar("123");
        Assert.Equal(123L, back.AsInt64());
    }

    [Fact]
    public void GD_Random_Works()
    {
        var r = GD.RandiRange(5, 10);
        Assert.InRange(r, 5, 10);
        var f = GD.Randf();
        Assert.InRange(f, 0.0, 1.0);
    }

    [Fact]
    public void GD_InstanceFromId_ResolvesIdentity()
    {
        var node = new Node();
        try
        {
            Assert.Same(node, GD.InstanceFromId(unchecked((long)node.InstanceId)));
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void StaticSingletons_AreStaticClasses()
    {
        // GodotSharp shape: no .Singleton needed for method calls.
        var expectedOs = OperatingSystem.IsWindows() ? "Windows"
            : OperatingSystem.IsLinux() ? "Linux"
            : OperatingSystem.IsMacOS() ? "macOS"
            : "?";
        Assert.Equal(expectedOs, OS.GetName());
        Assert.True(Time.GetTicksMsec() >= 0);
        Assert.True(Godot.Engine.GetPhysicsFrames() >= 0);
        Assert.True(typeof(Godot.Engine).IsAbstract && typeof(Godot.Engine).IsSealed); // static class
    }

    [Fact]
    public void EnumMembers_AreGodotSharpNamed()
    {
        Assert.Equal(4L, (long)Node.ProcessModeEnum.Disabled);
        Assert.Equal((VariantType)2, VariantType.Int);
        Assert.Equal(0L, (long)Error.Ok);
        Assert.Equal((long)Key.Escape, (long)Key.Escape);
    }

    [Fact]
    public void DefaultArguments_MakeTrailingArgsOptional()
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var child = new Node();
        // GodotSharp-style single-argument AddChild (2 trailing defaults).
        root.AddChild(child);
        Assert.Same(root, child.GetParent());
        root.RemoveChild(child);
        child.Free();
    }
}
