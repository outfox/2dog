using twodog;

namespace twodog.tests.EngineTests;

// Pure path resolution - no engine boot, no collection needed. The tests
// toggle the one input ResolveContent() keys on: a .pck named after the
// running executable (xunit v3 runs the test project as its own exe).
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
