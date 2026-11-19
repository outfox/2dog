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
        // The loader keeps a native reference to the cached resource without asking for a wrapper.
        var requested = BeforeTheFinalizer<Resource, Error>(path, () => ResourceLoader.LoadThreadedRequest(path));

        Assert.Equal(Error.Ok, requested);
        // The loader holds the only reference now; handing it out references the resource once more.
        var resource = ResourceLoader.LoadThreadedGet(path);
        Assert.Equal(path, resource.ResourcePath);
        Assert.Empty(godot.Errors.Drain());
    }

    [Fact]
    public void ReplacementWrapperOutlivesTheCollectedWrappersFinalizer()
    {
        const string path = "res://collected-wrapper-replacement.tres";
        var replacement = BeforeTheFinalizer<Resource, Resource>(path, () => ResourceLoader.Load(path));

        Assert.Same(replacement, ResourceLoader.Load(path));
        Assert.Empty(godot.Errors.Drain());
    }

    [Fact]
    public void ScriptInstanceIsRecreatedAfterCollection()
    {
        const string path = "res://collected-wrapper-script.tres";
        // The collected instance's finalizer finds the resource referenced and gives it a new managed instance.
        var requested = BeforeTheFinalizer<ScriptedResource, Error>(
            path, () => ResourceLoader.LoadThreadedRequest(path));

        Assert.Equal(Error.Ok, requested);
        var resource = ResourceLoader.LoadThreadedGet(path);
        Assert.IsType<ScriptedResource>(resource);
        Assert.Same(resource, ResourceLoader.Load(path));
        Assert.Empty(godot.Errors.Drain());
    }

    /// <summary>
    /// Caches a new <typeparamref name="T"/> under <paramref name="path"/>, collects its wrapper and runs
    /// <paramref name="whileCollected"/> before that wrapper's finalizer, which has run when this returns.
    /// </summary>
    private static TResult BeforeTheFinalizer<T, TResult>(string path, Func<TResult> whileCollected)
        where T : Resource, new()
    {
        var stall = FinalizerStall.Begin();
        try
        {
            DropCachedResource<T>(path);
            GC.Collect();
            return whileCollected();
        }
        finally
        {
            stall.Release();
            GC.WaitForPendingFinalizers();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DropCachedResource<T>(string path) where T : Resource, new() => new T().TakeOverPath(path);

    private sealed partial class ScriptedResource : Resource;
}
