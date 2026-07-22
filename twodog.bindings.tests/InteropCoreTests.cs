using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

/// <summary>Layout checks need no engine; they are pure sizeof asserts.</summary>
public class MathLayoutTests
{
    [Fact]
    public void MathStructs_MatchEngineFloat64Layout() => MathLayout.Validate();
}

[Collection(nameof(GodotBindingsCollection))]
public unsafe class InterfaceTests(GodotBindingsFixture godot)
{
    [Fact]
    public void AllProcsResolved()
    {
        Assert.True(GdExtensionHost.Loaded);
        Assert.Empty(GdExtensionInterface.MissingProcs);
    }

    [Fact]
    public void EngineVersion_IsFork471()
    {
        GDExtensionGodotVersion2 v = default;
        GdExtensionInterface.GetGodotVersion2((nint)(&v));
        Assert.Equal(4u, v.major);
        Assert.Equal(7u, v.minor);
    }

    [Fact]
    public void InstanceAndLibraryTokens_AreLive()
    {
        Assert.NotEqual(0, godot.InstancePtr);
        Assert.NotEqual(0, GdExtensionHost.Library);
    }
}

[Collection(nameof(GodotBindingsCollection))]
public unsafe class StringNameTests
{
    [Fact]
    public void Get_SameString_IsInterned()
    {
        var a = StringNames.Get("interned_test_name");
        var b = StringNames.Get("interned_test_name");
        Assert.Equal(a, b);
        Assert.Equal(a.Opaque, b.Opaque);
        Assert.True(a == b);
    }

    [Fact]
    public void Get_DifferentStrings_Differ()
    {
        Assert.NotEqual(StringNames.Get("name_one"), StringNames.Get("name_two"));
    }

    [Fact]
    public void Get_Unicode_UsesUtf8PathAndInterns()
    {
        var a = StringNames.Get("änlaut_测试_🐕");
        var b = StringNames.Get("änlaut_测试_🐕");
        Assert.Equal(a.Opaque, b.Opaque);
        Assert.NotEqual(0UL, a.Opaque);
    }

    [Fact]
    public void ReadAndDestroy_RoundtripsOwnedName()
    {
        // Create an OWNED StringName directly (not via the static cache).
        var bytes = System.Text.Encoding.UTF8.GetBytes("owned_roundtrip_name");
        ulong opaque = 0;
        fixed (byte* p = bytes)
        {
            GdExtensionInterface.StringNameNewWithUtf8CharsAndLen((nint)(&opaque), (nint)p, bytes.Length);
        }

        var read = StringNames.ReadAndDestroy(ref opaque);
        Assert.Equal("owned_roundtrip_name", read);
        Assert.Equal(0UL, opaque);
    }

    [Fact]
    public void HashCode_ConsistentWithEquality()
    {
        var a = StringNames.Get("hash_test");
        var b = StringNames.Get("hash_test");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}

[Collection(nameof(GodotBindingsCollection))]
public class NativeStringTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("ünïcödé 测试 🎮 \n tab\t")]
    public void CreateReadDestroy_Roundtrips(string value)
    {
        var s = NativeString.Create(value);
        try
        {
            Assert.Equal(value, NativeString.Read(in s));
        }
        finally
        {
            NativeString.Destroy(ref s);
        }
        Assert.Equal(0UL, s);
    }

    [Fact]
    public void Read_LongString_UsesHeapPath()
    {
        var value = new string('x', 10_000) + "🐈";
        var s = NativeString.Create(value);
        Assert.Equal(value, NativeString.ReadAndDestroy(ref s));
    }

    [Fact]
    public void Read_DoesNotConsume()
    {
        var s = NativeString.Create("stable");
        Assert.Equal("stable", NativeString.Read(in s));
        Assert.Equal("stable", NativeString.Read(in s));
        NativeString.Destroy(ref s);
    }
}

[Collection(nameof(GodotBindingsCollection))]
public class VariantTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bool_Roundtrips(bool value)
    {
        var v = Variants.FromBool(value);
        Assert.Equal(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_BOOL, Variants.TypeOf(in v));
        Assert.Equal(value, Variants.ToBool(in v));
        Variants.Destroy(ref v);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Int_Roundtrips(long value)
    {
        var v = Variants.FromInt(value);
        Assert.Equal(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_INT, Variants.TypeOf(in v));
        Assert.Equal(value, Variants.ToInt(in v));
        Variants.Destroy(ref v);
    }

    [Fact]
    public void Float_Roundtrips()
    {
        var v = Variants.FromFloat(-1234.5678);
        Assert.Equal(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_FLOAT, Variants.TypeOf(in v));
        Assert.Equal(-1234.5678, Variants.ToFloat(in v));
        Variants.Destroy(ref v);
    }

    [Fact]
    public void String_Roundtrips()
    {
        var v = Variants.FromString("variant ünïcödé 🐕");
        Assert.Equal(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_STRING, Variants.TypeOf(in v));
        Assert.Equal("variant ünïcödé 🐕", Variants.ToManagedString(in v));
        Variants.Destroy(ref v);
    }

    [Fact]
    public void Call_DispatchesHashFree()
    {
        using var rc = new RefCounted();
        // FromObject on a RefCounted takes a variant-owned reference.
        var self = Variants.FromObject(rc.NativePtr);
        var ret = Variants.Call(ref self, StringNames.Get("get_reference_count"));
        Assert.Equal(2, Variants.ToInt(in ret)); // wrapper ref + variant ref
        Variants.Destroy(ref ret);
        Variants.Destroy(ref self);
        Assert.Equal(1, rc.GetReferenceCount());
    }

    [Fact]
    public void Call_UnknownMethod_Throws()
    {
        using var rc = new RefCounted();
        var self = Variants.FromObject(rc.NativePtr);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                var local = self;
                var ret = Variants.Call(ref local, StringNames.Get("no_such_method_here"));
                Variants.Destroy(ref ret);
            });
        }
        finally
        {
            Variants.Destroy(ref self);
        }
    }
}
