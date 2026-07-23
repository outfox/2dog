using Godot;
using Godot.NativeInterop;
using twodog.Hosting.Runtime;
using twodog.Hosting.Xunit;
using Environment = System.Environment;

namespace twodog.bindings.tests;

// Pilot for parallel test collections (MULTI-INSTANCE-PLAN.md phase 4): these
// collections each own an engine instance in its own ALC and run in parallel
// with each other (xunit.runner.json: parallelizeTestCollections=true). The
// classic GodotBindingsCollection keeps DisableParallelization=true and its
// exclusive default-ALC engine; hosted instances use separate native copies,
// so the two models coexist in one test process.
//
// Authoring model: engine work lives in IEngineScenario implementations
// (executed inside the instance ALC, on its engine thread); tests assert on
// the returned report string.

public class HostedEngineFixture : EngineInstanceFixture
{
    // Same native-variant selection convention as GodotBindingsFixture.
    protected override string Variant => Environment.GetEnvironmentVariable("TWODOG_VARIANT") ?? "debug";
}

public sealed class HostedAlphaFixture : HostedEngineFixture
{
    protected override string Tag => "bindings-hosted-alpha";
}

public sealed class HostedBetaFixture : HostedEngineFixture
{
    protected override string Tag => "bindings-hosted-beta";
}

[CollectionDefinition(nameof(HostedAlphaCollection))]
public sealed class HostedAlphaCollection : ICollectionFixture<HostedAlphaFixture>;

[CollectionDefinition(nameof(HostedBetaCollection))]
public sealed class HostedBetaCollection : ICollectionFixture<HostedBetaFixture>;

public sealed class NodeLifecycleScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument)
    {
        var root = session.Tree.Root ?? throw new InvalidOperationException("no root");
        var before = root.GetChildCount();
        var node = new Node2D { Name = $"hosted_{argument}" };
        node.Position = new Vector2(1.5f, -2.5f);
        root.AddChild(node);
        var delta = root.GetChildCount() - before;
        var pos = node.Position;
        var name = $"{node.Name}";
        node.Free();
        var freed = !node.IsValid;
        return $"delta={delta};name={name};pos={pos.X},{pos.Y};freed={freed}";
    }
}

public sealed class RefCountedLifetimeScenario : IEngineScenario
{
    public string Run(EngineSession session, string? argument)
    {
        long rc;
        using (var refCounted = new RefCounted())
        {
            rc = refCounted.GetReferenceCount();
        }
        var releasedBefore = DisposalQueue.Released;
        DisposalQueue.Drain();
        return $"rc={rc};released={DisposalQueue.Released - releasedBefore}";
    }
}

[Collection(nameof(HostedAlphaCollection))]
public sealed class HostedAlphaTests(HostedAlphaFixture fixture)
{
    [Fact]
    public void NodeLifecycleRoundtrips()
    {
        var report = fixture.Run<NodeLifecycleScenario>("alpha");
        Assert.Equal("delta=1;name=hosted_alpha;pos=1.5,-2.5;freed=True", report);
    }

    [Fact]
    public void RefCountedReleasesThroughDrain()
    {
        Assert.Equal("rc=1;released=1", fixture.Run<RefCountedLifetimeScenario>());
    }
}

[Collection(nameof(HostedBetaCollection))]
public sealed class HostedBetaTests(HostedBetaFixture fixture)
{
    [Fact]
    public void NodeLifecycleRoundtrips()
    {
        var report = fixture.Run<NodeLifecycleScenario>("beta");
        Assert.Equal("delta=1;name=hosted_beta;pos=1.5,-2.5;freed=True", report);
    }

    [Fact]
    public void RefCountedReleasesThroughDrain()
    {
        Assert.Equal("rc=1;released=1", fixture.Run<RefCountedLifetimeScenario>());
    }
}
