using System.Runtime.Loader;

namespace twodog.Hosting.Tests;

/// <summary>An exception type the tests can also instantiate from a second ALC
/// (a reloaded copy of this test assembly) to stand in for instance-ALC-typed
/// failures without booting an instance.</summary>
public sealed class ForeignMarkerException(string message) : Exception(message);

public sealed class ExceptionSanitizerTests
{
    [Fact]
    public void DefaultAlcChainPassesThroughUnchanged()
    {
        var e = new InvalidOperationException("outer", new IOException("inner"));
        Assert.Same(e, ExceptionSanitizer.Sanitize(e));
    }

    [Fact]
    public void ForeignAlcExceptionIsFlattenedToEngineInstanceException()
    {
        var flattened = Assert.IsType<EngineInstanceException>(
            ExceptionSanitizer.Sanitize(CreateForeign("boom-across-alcs")));
        Assert.Equal(typeof(ForeignMarkerException).FullName, flattened.OriginalType);
        Assert.Contains("boom-across-alcs", flattened.Message);
    }

    [Fact]
    public void ForeignInnerExceptionIsDetectedThroughTheChain()
    {
        var e = new InvalidOperationException("outer", CreateForeign("inner-foreign"));
        var flattened = Assert.IsType<EngineInstanceException>(ExceptionSanitizer.Sanitize(e));
        Assert.Contains("inner-foreign", flattened.Message);
    }

    [Fact]
    public void AggregateWithOneForeignBranchIsFlattened()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("fine"), CreateForeign("foreign-branch"));
        var flattened = Assert.IsType<EngineInstanceException>(ExceptionSanitizer.Sanitize(aggregate));
        Assert.Contains("foreign-branch", flattened.Message);
    }

    [Fact]
    public void AllDefaultAggregatePassesThrough()
    {
        var aggregate = new AggregateException(new InvalidOperationException(), new IOException());
        Assert.Same(aggregate, ExceptionSanitizer.Sanitize(aggregate));
    }

    [Fact]
    public void ChainDeeperThanTheCycleGuardStillPassesThrough()
    {
        // The depth guard bounds the walk (cycle insurance); default-ALC chains
        // deeper than it must still pass through rather than loop or flatten.
        Exception e = new InvalidOperationException("leaf");
        for (var i = 0; i < 40; i++) e = new InvalidOperationException($"level {i}", e);
        Assert.Same(e, ExceptionSanitizer.Sanitize(e));
    }

    private static Exception CreateForeign(string message)
    {
        var alc = new AssemblyLoadContext($"sanitizer-foreign-{Guid.NewGuid():N}", isCollectible: true);
        var assembly = alc.LoadFromAssemblyPath(typeof(ForeignMarkerException).Assembly.Location);
        var type = assembly.GetType(typeof(ForeignMarkerException).FullName!, throwOnError: true)!;
        return (Exception)Activator.CreateInstance(type, message)!;
    }
}
