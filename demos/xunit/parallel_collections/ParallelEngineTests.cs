using System.Collections.Concurrent;

namespace ParallelCollectionsDemo;

/// <summary>Every collection records its instance's native library path here
/// (default-ALC static, shared by all tests); once two or more exist they must
/// be pairwise distinct - each instance runs its own physical libgodot copy.</summary>
public static class NativeCopyRegistry
{
    private static readonly ConcurrentDictionary<string, string> Paths = new();

    public static void RecordAndAssertDisjoint(string tag, string path)
    {
        Paths[tag] = path;
        if (Paths.Count < 2) return; // sibling collections not booted yet - nothing to compare
        Assert.Equal(Paths.Count, Paths.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

[Collection(nameof(AlphaCollection))]
public sealed class AlphaEngineTests(AlphaEngineFixture fixture)
{
    [Fact]
    public void SpawnsNodesInItsOwnTree()
    {
        HostGuard.SkipUnlessSupported();
        var report = fixture.Run<SpawnNodeScenario>("alpha");
        Assert.Contains("delta=1", report);
        Assert.Contains("name=spawned_by_alpha", report);
    }

    [Fact]
    public void RunsAnIsolatedEngineInstance()
    {
        HostGuard.SkipUnlessSupported();
        var report = fixture.Run<WhoAmIScenario>();
        Assert.Contains("project=2dog-demo-alpha", report); // generated scratch projects are named 2dog-<tag>
        Assert.Contains("alcIsDefault=False", report);
        NativeCopyRegistry.RecordAndAssertDisjoint("alpha", fixture.Run<NativePathScenario>());
    }
}

[Collection(nameof(BetaCollection))]
public sealed class BetaEngineTests(BetaEngineFixture fixture)
{
    [Fact]
    public void SpawnsNodesInItsOwnTree()
    {
        HostGuard.SkipUnlessSupported();
        var report = fixture.Run<SpawnNodeScenario>("beta");
        Assert.Contains("delta=1", report);
        Assert.Contains("name=spawned_by_beta", report);
    }

    [Fact]
    public void RunsAnIsolatedEngineInstance()
    {
        HostGuard.SkipUnlessSupported();
        var report = fixture.Run<WhoAmIScenario>();
        Assert.Contains("project=2dog-demo-beta", report);
        Assert.Contains("alcIsDefault=False", report);
        NativeCopyRegistry.RecordAndAssertDisjoint("beta", fixture.Run<NativePathScenario>());
    }
}

[Collection(nameof(CopiedProjectCollection))]
public sealed class CopiedProjectTests(CopiedProjectFixture fixture)
{
    [Fact]
    public void BootsTheCopiedProjectsMainScene()
    {
        HostGuard.SkipUnlessSupported();
        var report = fixture.Run<ReadGreetingScenario>();
        Assert.Contains("scene=Main", report);
        Assert.Contains("greeting=hello from a copied project", report);
    }

    [Fact]
    public void KeepsItsOwnProjectSettings()
    {
        HostGuard.SkipUnlessSupported();
        var report = fixture.Run<WhoAmIScenario>();
        Assert.Contains("project=Parallel Collections Demo", report);
        NativeCopyRegistry.RecordAndAssertDisjoint("copied", fixture.Run<NativePathScenario>());
    }
}
