namespace twodog.tests.HostingTests;

/// <summary>Direct tests of the slot-explicit pool core (internal). Each test
/// uses its own random source file, so its pool key never collides with the
/// engine copies other tests create.</summary>
public sealed class NativePoolTests : IDisposable
{
    private readonly string _source;
    private readonly List<string> _copies = [];

    public NativePoolTests()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), $"2dog-pool-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        _source = Path.Combine(sourceDir, "native.bin");
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(_source, payload);
    }

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(_source)!, recursive: true);
        // Slot dirs and the per-source identity dir; quiet best-effort cleanup.
        foreach (var keyDir in _copies.Select(c => Path.GetDirectoryName(Path.GetDirectoryName(c))!).Distinct())
        {
            try
            {
                Directory.Delete(keyDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private string Acquire(int slot)
    {
        var copy = EngineHost.AcquireNativeCopy(_source, slot);
        lock (_copies)
        {
            _copies.Add(copy);
        }
        return copy;
    }

    [Fact]
    public void CopiesCarrySlotDistinctNamesAndFullContent()
    {
        var copy = Acquire(91_000);
        Assert.NotEqual(_source, copy);
        Assert.Contains("slot-91000", copy);
        Assert.Equal(File.ReadAllBytes(_source), File.ReadAllBytes(copy));
    }

    [Fact]
    public void DawnDependenciesTravelWithTheCopyAndInvalidateItsIdentity()
    {
        var name = OperatingSystem.IsWindows() ? "godot_webgpu_dawn.dll" : "libgodot_webgpu_dawn.so";
        var dependency = Path.Combine(Path.GetDirectoryName(_source)!, name);
        File.WriteAllBytes(dependency, [1, 2, 3]);
        var first = Acquire(91_002);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(first)!, name)));
        File.WriteAllBytes(dependency, [4, 5, 6, 7]);
        var second = Acquire(91_002);
        Assert.NotEqual(first, second);
        Assert.Equal([4, 5, 6, 7], File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(second)!, name)));
    }

    [Fact]
    public void PartialCopiesAreEvicted()
    {
        var copy = Acquire(91_001);
        File.WriteAllBytes(copy, new byte[16]); // truncated leftover of a crashed process
        var again = Acquire(91_001);
        Assert.Equal(copy, again);
        Assert.Equal(new FileInfo(_source).Length, new FileInfo(again).Length);
    }

    [Fact]
    public void ConcurrentSlotsYieldDistinctVerifiedCopies()
    {
        var copies = Enumerable.Range(91_100, 8)
            .AsParallel().WithDegreeOfParallelism(8)
            .Select(Acquire)
            .ToArray();
        Assert.Equal(copies.Length, copies.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(copies, c => Assert.Equal(new FileInfo(_source).Length, new FileInfo(c).Length));
    }
}
