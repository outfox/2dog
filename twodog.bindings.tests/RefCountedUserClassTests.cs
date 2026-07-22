using System.Runtime.CompilerServices;
using Godot;
using Godot.NativeInterop;
using Array = Godot.Collections.Array;

namespace twodog.bindings.tests;

/// <summary>A custom-resource-style user class with exports and signals.</summary>
public partial class GameData : Resource
{
    [Export] public int Score { get; set; } = 7;
    public string Tag = "";

    [Signal] public delegate void TagChangedEventHandler(string tag);
}

public partial class DerivedData : GameData;

/// <summary>
/// RefCounted user classes (custom resources): the managed instance owns
/// exactly one engine ref; the instance GCHandle stays weak (its pointer is
/// engine-held and unswappable) while the binding slot's strong/weak flip
/// roots the managed object whenever the engine holds additional refs.
/// create_instance3 contract: engine-first creation returns the construct
/// refcount to the engine caller, so the managed side takes its own on top.
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class RefCountedUserClassTests
{
    private static void EnsureRegistered()
    {
        ClassRegistry.Register<GameData>();
        ClassRegistry.Register<DerivedData>();
    }

    [Fact]
    public void Register_Succeeds_AndVisibleInClassDb()
    {
        EnsureRegistered();
        Assert.True(ClassDB.ClassExists("GameData"));
        Assert.True(ClassDB.IsParentClass("GameData", "Resource"));
    }

    [Fact]
    public void NewFromCSharp_OwnsExactlyOneRef()
    {
        EnsureRegistered();
        using var data = new GameData();
        Assert.True(data.IsRefCounted);
        Assert.Equal(1, data.GetReferenceCount());
        Assert.Equal("GameData", data.GetClass());
        Assert.True(data.IsValid);
        DisposalQueue.Drain();
    }

    [Fact]
    public void Dispose_ReleasesRef_AndObjectDies()
    {
        EnsureRegistered();
        var data = new GameData();
        var id = data.InstanceId;
        data.Dispose();
        DisposalQueue.Drain();
        Assert.False(IsAlive(id));
    }

    [Fact]
    public void Collected_ReleasesRef_AndObjectDies()
    {
        EnsureRegistered();
        var (id, weak) = CreateUnrooted();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        DisposalQueue.Drain();
        Assert.False(IsAlive(id));
        Assert.False(weak.IsAlive, "managed instance leaked after native death");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ulong id, WeakReference weak) CreateUnrooted()
    {
        var data = new GameData();
        Assert.Equal(1, data.GetReferenceCount());
        return (data.InstanceId, new WeakReference(data));
    }

    [Fact]
    public void EngineRef_KeepsManagedStateAlive_AcrossGc()
    {
        EnsureRegistered();
        using var holder = new Array();
        var id = CreateIntoArray(holder);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        DisposalQueue.Drain();

        Assert.True(IsAlive(id)); // the array's engine ref keeps it alive

        GameData data;
        using (var element = holder[0])
        {
            data = Assert.IsType<GameData>(element.AsGodotObject());
            Assert.Equal("gc_survivor_data", data.Tag);   // managed field state intact
            Assert.Equal(1234, data.Score);
        }

        holder.Clear();
        Assert.Equal(1, data.GetReferenceCount()); // back to the managed-owned ref
        data.Dispose();
        DisposalQueue.Drain();
        Assert.False(IsAlive(id));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong CreateIntoArray(Array holder)
    {
        var data = new GameData
        {
            Tag = "gc_survivor_data",
            Score = 1234,
        };
        using var v = Variant.From(data);
        holder.Add(v);
        Assert.True(data.GetReferenceCount() > 1);
        Assert.True(InstanceBindings.DebugIsStrong(data.NativePtr));
        return data.InstanceId;
    }

    [Fact]
    public void BindingStrength_FlipsWithEngineRefs()
    {
        EnsureRegistered();
        using var data = new GameData();
        Assert.False(InstanceBindings.DebugIsStrong(data.NativePtr));

        using (var v = Variant.From(data))
        {
            _ = v;
            Assert.Equal(2, data.GetReferenceCount());
            Assert.True(InstanceBindings.DebugIsStrong(data.NativePtr));
        }
        Assert.Equal(1, data.GetReferenceCount());
        Assert.False(InstanceBindings.DebugIsStrong(data.NativePtr));
        DisposalQueue.Drain();
    }

    [Fact]
    public void EngineFirstCreation_RefAccounting()
    {
        EnsureRegistered();
        // ConstructRaw = the engine-caller path (create_instance3): the
        // construct ref transfers to us-the-caller, the managed instance
        // takes its own -> rc == 2.
        var ptr = InstanceBindings.ConstructRaw("GameData");
        Assert.NotEqual(0, ptr);
        Assert.Equal(2, RefCountedNative.GetReferenceCount(ptr));

        var data = Assert.IsType<GameData>(InstanceBindings.GetOrCreate(ptr, adoptRef: false));
        var id = data.InstanceId;

        // Release the caller's transferred ref: only the managed ref remains.
        RefCountedNative.Unreference(ptr);
        Assert.Equal(1, data.GetReferenceCount());
        Assert.True(data.IsValid);

        data.Dispose();
        DisposalQueue.Drain();
        Assert.False(IsAlive(id));
    }

    [Fact]
    public void TypedInstantiate_ViaClassDb()
    {
        EnsureRegistered();
        using var v = ClassDB.Instantiate("GameData");
        var data = Assert.IsType<GameData>(v.AsGodotObject());
        Assert.Equal(2, data.GetReferenceCount()); // variant's ref + managed ref
        Assert.Equal(7, data.Score);               // C# initializer ran
        data.Dispose();
        DisposalQueue.Drain();
    }

    [Fact]
    public void ExportedProperty_EngineRoundtrip_OnRefCounted()
    {
        EnsureRegistered();
        using var data = new GameData();
        using Variant v = 42;
        data.Set("score", v);
        Assert.Equal(42, data.Score);
        using var got = data.Get("score");
        Assert.Equal(42, got.AsInt32());
        DisposalQueue.Drain();
    }

    [Fact]
    public void Signal_OnRefCounted_Roundtrips()
    {
        EnsureRegistered();
        using var data = new GameData();
        string? received = null;
        data.TagChanged += tag => received = tag;
        data.EmitSignalTagChanged("fresh_tag");
        Assert.Equal("fresh_tag", received);
        DisposalQueue.Drain();
    }

    [Fact]
    public void PassedThroughEngine_PreservesIdentity()
    {
        EnsureRegistered();
        using var data = new GameData();
        using var arr = new Array();
        using (var v = Variant.From(data))
        {
            arr.Add(v);
        }
        using var back = arr[0];
        Assert.Same(data, back.AsGodotObject());
        arr.Clear();
        DisposalQueue.Drain();
    }

    [Fact]
    public void DerivedUserClass_Works()
    {
        EnsureRegistered();
        Assert.True(ClassDB.IsParentClass("DerivedData", "GameData"));
        using var derived = new DerivedData();
        Assert.Equal("DerivedData", derived.GetClass());
        Assert.Equal(1, derived.GetReferenceCount());
        Assert.Equal(7, derived.Score); // inherited export initializer
        DisposalQueue.Drain();
    }

    private static unsafe bool IsAlive(ulong id) => GdExtensionInterface.ObjectGetInstanceFromId(id) != 0;
}
