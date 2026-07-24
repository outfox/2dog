using twodog.Hosting;
using twodog.Hosting.Xunit;

namespace ParallelCollectionsDemo;

// One EngineInstanceFixture subclass per collection: each collection gets its own
// isolated engine instance, and because the collection definitions do NOT set
// DisableParallelization, xUnit runs the collections at the same time.

public sealed class AlphaEngineFixture : EngineInstanceFixture
{
    protected override string Tag => "demo-alpha";
}

public sealed class BetaEngineFixture : EngineInstanceFixture
{
    protected override string Tag => "demo-beta";
}

/// <summary>Boots from a copy of demos/parallel_collections/project instead of
/// the generated minimal scratch project.</summary>
public sealed class CopiedProjectFixture : EngineInstanceFixture
{
    protected override string Tag => "demo-copied";
    protected override string? SourceProjectDir => Path.Combine(AppContext.BaseDirectory, "project");
}

[CollectionDefinition(nameof(AlphaCollection))]
public sealed class AlphaCollection : ICollectionFixture<AlphaEngineFixture>;

[CollectionDefinition(nameof(BetaCollection))]
public sealed class BetaCollection : ICollectionFixture<BetaEngineFixture>;

[CollectionDefinition(nameof(CopiedProjectCollection))]
public sealed class CopiedProjectCollection : ICollectionFixture<CopiedProjectFixture>;

/// <summary>In-process hosting is platform-gated (macOS fails closed), so every
/// test skips first; the fixture itself no-ops on unsupported platforms.</summary>
internal static class HostGuard
{
    public static void SkipUnlessSupported() =>
        Assert.SkipWhen(!EngineHost.IsSupported, "In-process engine hosting is unsupported on this platform.");
}
