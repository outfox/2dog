using System.Runtime.CompilerServices;
using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.EngineTests;

/// <summary>
/// A collected RefCounted wrapper keeps its native reference until its finalizer runs. Natives can still reach the
/// object in that window, here through the resource cache while a stall holds the finalizer thread.
/// </summary>
[Collection<HeadlessCollection>]
public class CollectedWrapperTests(HeadlessFixture godot)
{
    [Fact]
    public void NativeReferencesAfterCollectionReportNoErrors()
    {
        const string path = "res://collected-wrapper-native-references.tres";
        var stall = FinalizerStall.Begin();
        try
        {
            DropCachedResource<Resource>(path);
            GC.Collect();
            // The loader keeps a native reference to the cached resource without asking for a wrapper.
            Assert.Equal(Error.Ok, ResourceLoader.LoadThreadedRequest(path));
        }
        finally
        {
            stall.Release();
        }

        GC.WaitForPendingFinalizers();
        // The loader holds the only reference now; handing it out references the resource once more.
        var resource = ResourceLoader.LoadThreadedGet(path);

        Assert.Equal(path, resource.ResourcePath);
        Assert.Empty(godot.Errors.Drain());
    }

    [Fact]
    public void ReplacementWrapperOutlivesTheCollectedWrappersFinalizer()
    {
        const string path = "res://collected-wrapper-replacement.tres";
        Resource replacement;
        var stall = FinalizerStall.Begin();
        try
        {
            DropCachedResource<Resource>(path);
            GC.Collect();
            replacement = ResourceLoader.Load(path);
        }
        finally
        {
            stall.Release();
        }

        GC.WaitForPendingFinalizers();

        Assert.Same(replacement, ResourceLoader.Load(path));
        Assert.Empty(godot.Errors.Drain());
    }

    [Fact]
    public void ScriptInstanceIsRecreatedAfterCollection()
    {
        const string path = "res://collected-wrapper-script.tres";
        var stall = FinalizerStall.Begin();
        try
        {
            DropCachedResource<ScriptedResource>(path);
            GC.Collect();
            Assert.Equal(Error.Ok, ResourceLoader.LoadThreadedRequest(path));
        }
        finally
        {
            stall.Release();
        }

        // The collected instance's finalizer finds the resource referenced and gives it a new managed instance.
        GC.WaitForPendingFinalizers();
        var resource = ResourceLoader.LoadThreadedGet(path);

        Assert.IsType<ScriptedResource>(resource);
        Assert.Same(resource, ResourceLoader.Load(path));
        Assert.Empty(godot.Errors.Drain());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DropCachedResource<T>(string path) where T : Resource, new() => new T().TakeOverPath(path);

    private sealed partial class ScriptedResource : Resource;
}
