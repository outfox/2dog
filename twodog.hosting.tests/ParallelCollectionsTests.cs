using System.Collections.Concurrent;
using twodog.Hosting.Xunit;

namespace twodog.Hosting.Tests;

// Two collections, each with its own engine instance, running IN PARALLEL
// (xunit.runner.json: parallelizeTestCollections=true). This is the layout the
// whole multi-instance feature exists for.

public sealed class AlphaEngineFixture : EngineInstanceFixture
{
    protected override string Tag => "alpha";
}

public sealed class BetaEngineFixture : EngineInstanceFixture
{
    protected override string Tag => "beta";
}

[CollectionDefinition(nameof(AlphaCollection))]
public sealed class AlphaCollection : ICollectionFixture<AlphaEngineFixture>;

[CollectionDefinition(nameof(BetaCollection))]
public sealed class BetaCollection : ICollectionFixture<BetaEngineFixture>;

/// <summary>Both collections' fixtures record their native copy here (default
/// ALC static), so either side can assert disjointness once both exist.</summary>
public static class NativePathRegistry
{
    public static readonly ConcurrentDictionary<string, string> Paths = new();

    public static void AssertDisjoint()
    {
        if (Paths.Count < 2) return; // sibling collection not booted yet - nothing to compare
        Assert.Equal(Paths.Count, Paths.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

[Collection(nameof(AlphaCollection))]
public sealed class AlphaEngineTests(AlphaEngineFixture fixture)
{
    [Fact]
    public void AddsAndRemovesNodes()
    {
        var report = fixture.Run<AddNodeScenario>("alpha");
        Assert.Contains("delta=1", report);
        Assert.Contains("name=n_alpha", report);
    }

    [Fact]
    public void SurvivesNodeChurnAcrossFrames()
    {
        var report = fixture.Run<ChurnScenario>("30");
        Assert.Contains("frames=30", report);
        Assert.Contains("rc=1", report);
        Assert.Contains("childDelta=0", report);
    }

    [Fact]
    public void UsesItsOwnNativeCopy()
    {
        var path = fixture.Run<NativePathScenario>();
        Assert.EndsWith(OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so", path);
        NativePathRegistry.Paths["alpha"] = path;
        NativePathRegistry.AssertDisjoint();
    }
}

[Collection(nameof(BetaCollection))]
public sealed class BetaEngineTests(BetaEngineFixture fixture)
{
    [Fact]
    public void AddsAndRemovesNodes()
    {
        var report = fixture.Run<AddNodeScenario>("beta");
        Assert.Contains("delta=1", report);
        Assert.Contains("name=n_beta", report);
    }

    [Fact]
    public void SurvivesNodeChurnAcrossFrames()
    {
        var report = fixture.Run<ChurnScenario>("30");
        Assert.Contains("frames=30", report);
        Assert.Contains("childDelta=0", report);
    }

    [Fact]
    public void UsesItsOwnNativeCopy()
    {
        var path = fixture.Run<NativePathScenario>();
        NativePathRegistry.Paths["beta"] = path;
        NativePathRegistry.AssertDisjoint();
    }
}
