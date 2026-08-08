using twodog;

namespace twodog.tests.EngineTests;

// These tests toggle the executable-adjacent .pck used by ResolveContent(),
// so they must not overlap a test that constructs an engine without a path.
[CollectionDefinition(nameof(ResolveContentCollection), DisableParallelization = true)]
public class ResolveContentCollection;

[Collection(nameof(ResolveContentCollection))]
public class ResolveContentTests
{
    [Fact]
    public void ResolveContent_WithoutExeAdjacentPack_ReturnsProjectDir()
    {
        Assert.Equal(Engine.ResolveProjectDir(), Engine.ResolveContent());
    }

    [Fact]
    public void ResolveContent_WithExeAdjacentPack_ReturnsNull()
    {
        var exePath = Environment.ProcessPath;
        Assert.False(string.IsNullOrEmpty(exePath));

        var pckPath = Path.ChangeExtension(exePath, ".pck");
        File.WriteAllBytes(pckPath, []);
        try
        {
            Assert.Null(Engine.ResolveContent());
        }
        finally
        {
            File.Delete(pckPath);
        }
    }
}
